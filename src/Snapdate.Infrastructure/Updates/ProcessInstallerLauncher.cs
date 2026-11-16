using System.ComponentModel;
using System.Diagnostics;
using Snapdate.Application.Installation;
using Snapdate.Application.Ports;

namespace Snapdate.Infrastructure.Updates;

/// <summary>
/// Starts the verified Inno Setup installer silently. Snapdate never elevates itself (no <c>runas</c> verb): for an
/// all-users update Setup shows the administrator prompt and relaunches itself elevated. Nothing but the installer is
/// ever started.
/// </summary>
public sealed class ProcessInstallerLauncher : IInstallerLauncher
{
    /// <summary>Command line for the given scope, e.g. <c>/VERYSILENT … /CURRENTUSER /RESTARTAPP</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scope"/> is not an installed scope.</exception>
    internal static string BuildArguments(InstallScope scope) => scope switch
    {
        InstallScope.CurrentUser => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /RESTARTAPP",
        InstallScope.AllUsers => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS /RESTARTAPP",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Only installed scopes can be updated."),
    };

    public IStartedInstaller? Launch(string installerPath, InstallScope scope)
    {
        try
        {
            var info = new ProcessStartInfo(installerPath, BuildArguments(scope))
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty,
            };
            var process = Process.Start(info);
            return process is null ? null : new StartedProcessInstaller(process);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private sealed class StartedProcessInstaller(Process process) : IStartedInstaller
    {
        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }

        public void Dispose() => process.Dispose();
    }
}
