using System.Text;
using Snapdate.Application.Installation;
using Snapdate.Application.Ports;
using Snapdate.Domain.Versioning;

namespace Snapdate.Application.Updates;

/// <summary>
/// Downloads the installer of a release, verifies its signature and starts it silently (ADR 0004).
/// The installer is only started when every check passes; otherwise nothing runs and nothing is left on disk.
/// </summary>
/// <remarks>
/// <para>
/// Failures are reported through <see cref="UpdateInstallResult"/>, never as exceptions: a cancelled run yields
/// <see cref="UpdateInstallResult.Cancelled"/> (the use case catches <see cref="OperationCanceledException"/> itself).
/// </para>
/// <para>
/// <b>Contract for the UI.</b> After the installer has been started this method does <b>not</b> return until the
/// installer process has exited or <c>appShutdown</c> is signalled. The verified installer file stays open (nobody
/// can write, rename or delete it) for that whole time, which also covers Setup relaunching itself elevated for an
/// all-users update. Therefore the app must <b>not</b> exit by itself after starting the update: keep running (show
/// "installing…"), because Setup closes Snapdate itself through Windows' Restart Manager when it needs the exe.
/// When that close request arrives (window closing, session ending) the app signals <c>appShutdown</c> and lets the
/// call finish; the process exit then releases the lock. A declined administrator prompt ends the installer with a
/// non-zero code and yields <see cref="UpdateInstallResult.InstallerFailed"/>, so the user is told instead of the app
/// silently vanishing. Cancelling <c>cancellationToken</c> only affects download and verification, never a started installer.
/// </para>
/// </remarks>
public sealed class InstallUpdateUseCase
{
    /// <summary>Hard cap for the installer (the real one is about 40 MB).</summary>
    public const long MaxInstallerBytes = 300L * 1024 * 1024;

    /// <summary>Hard cap for the <c>.sig</c> file (88 base64 characters plus a newline).</summary>
    public const long MaxSignatureBytes = 1024;

    private readonly AppVersion _current;
    private readonly IInstallInfo _installInfo;
    private readonly IInstallLocationCheck _locationCheck;
    private readonly IProcessArchitecture _architecture;
    private readonly IUpdateDownloader _downloader;
    private readonly IUpdateSignatureVerifier _verifier;
    private readonly IInstallerLauncher _launcher;

    public InstallUpdateUseCase(
        AppVersion currentVersion,
        IInstallInfo installInfo,
        IInstallLocationCheck locationCheck,
        IProcessArchitecture architecture,
        IUpdateDownloader downloader,
        IUpdateSignatureVerifier verifier,
        IInstallerLauncher launcher)
    {
        _current = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _installInfo = installInfo ?? throw new ArgumentNullException(nameof(installInfo));
        _locationCheck = locationCheck ?? throw new ArgumentNullException(nameof(locationCheck));
        _architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <summary>
    /// True when the silent path can be offered for <paramref name="release"/>: installed (marker confirmed by the
    /// registry), a stable and newer release, a supported architecture. Cheap; the UI may use it to pick the button.
    /// </summary>
    public bool IsApplicable(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var install = _installInfo.Current;
        return install.Kind == InstallKind.Installed
            && install.Scope is InstallScope.CurrentUser or InstallScope.AllUsers
            && !release.IsDraft
            && !release.IsPreRelease
            && !release.Version.IsPreRelease
            && release.Version > _current
            && _architecture.Current is UpdateArchitecture.X64 or UpdateArchitecture.Arm64
            && _locationCheck.IsRegisteredForRunningExe(install.Scope);
    }

    /// <summary>Runs the update; see the class remarks for what the caller must and must not do.</summary>
    /// <param name="release">The release to install.</param>
    /// <param name="progress">Download progress of the installer.</param>
    /// <param name="cancellationToken">Cancels download and verification (not a started installer).</param>
    /// <param name="appShutdown">Signalled by the app when it is being closed; ends the wait for the started installer.</param>
    public async Task<UpdateInstallResult> ExecuteAsync(
        ReleaseInfo release,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken,
        CancellationToken appShutdown = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (!IsApplicable(release))
        {
            return UpdateInstallResult.NotApplicable;
        }

        var arch = _architecture.Current == UpdateArchitecture.X64 ? "x64" : "arm64";
        var installerName = $"Snapdate-Setup-v{release.Version}-win-{arch}.exe";
        var installer = release.Assets.FirstOrDefault(a => string.Equals(a.Name, installerName, StringComparison.Ordinal));
        var signature = release.Assets.FirstOrDefault(a => string.Equals(a.Name, installerName + ".sig", StringComparison.Ordinal));
        if (installer is null || signature is null
            || !IsInTagFolder(installer.DownloadUrl, release.Version) || !IsInTagFolder(signature.DownloadUrl, release.Version))
        {
            return UpdateInstallResult.AssetMissing;
        }

        if (installer.Size is <= 0 or > MaxInstallerBytes || signature.Size is <= 0 or > MaxSignatureBytes)
        {
            return UpdateInstallResult.DownloadFailed;
        }

        var scope = _installInfo.Current.Scope;
        IDownloadedFile? installerFile = null;
        IStartedInstaller? started = null;
        var keepFiles = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            string signatureText;
            using (var signatureFile = await _downloader.DownloadAsync(signature, MaxSignatureBytes, null, cancellationToken).ConfigureAwait(false))
            {
                signatureText = ReadText(signatureFile.Content);
            }

            installerFile = await _downloader.DownloadAsync(installer, MaxInstallerBytes, progress, cancellationToken).ConfigureAwait(false);

            // Verified from the very handle that stays open (read-shared only) until the process is started,
            // so the bytes that were checked are the bytes that run.
            if (!_verifier.Verify(installerFile.Content, signatureText))
            {
                return UpdateInstallResult.VerificationFailed;
            }

            started = _launcher.Launch(installerFile.Path, scope);
            if (started is null)
            {
                return UpdateInstallResult.LaunchFailed;
            }

            // From here on the installer runs from the file we keep locked; wait for it (or for our own shutdown).
            int exitCode;
            try
            {
                exitCode = await started.WaitForExitAsync(appShutdown).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                keepFiles = true; // the installer may still be running from it
                return UpdateInstallResult.InstallerRunning;
            }

            return exitCode == 0 ? UpdateInstallResult.Completed : UpdateInstallResult.InstallerFailed;
        }
        catch (OperationCanceledException)
        {
            return UpdateInstallResult.Cancelled;
        }
        catch (Exception ex) when (ex is UpdateDownloadException or IOException or UnauthorizedAccessException)
        {
            return UpdateInstallResult.DownloadFailed;
        }
        finally
        {
            started?.Dispose();
            if (keepFiles)
            {
                installerFile?.KeepOnDisk();
            }

            installerFile?.Dispose();
        }
    }

    /// <summary>The download URL must sit in the release's own tag folder (<c>v&lt;Version&gt;</c>).</summary>
    private static bool IsInTagFolder(Uri url, AppVersion version)
    {
        // "/", owner, repo, "releases", "download", tag, name
        var segments = url.Segments;
        return segments.Length == 7 && Uri.UnescapeDataString(segments[5].TrimEnd('/')) == "v" + version;
    }

    private static string ReadText(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
