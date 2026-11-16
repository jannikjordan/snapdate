using Snapdate.Application.Installation;
using Snapdate.Application.Ports;
using Snapdate.Application.Updates;
using Snapdate.Domain.Versioning;

namespace Snapdate.Application.Tests.Updates;

public sealed class InstallUpdateUseCaseTests
{
    private static readonly AppVersion Current = AppVersion.Parse("0.1.0");
    private static readonly AppVersion Newer = AppVersion.Parse("0.2.0");

    private sealed class FakeInstallInfo(InstallInfo info) : IInstallInfo
    {
        public InstallInfo Current { get; } = info;
    }

    private sealed class FakeLocation(bool ok) : IInstallLocationCheck
    {
        public InstallScope? Asked { get; private set; }

        public bool IsRegisteredForRunningExe(InstallScope scope)
        {
            Asked = scope;
            return ok;
        }
    }

    private sealed class FakeArch(UpdateArchitecture arch) : IProcessArchitecture
    {
        public UpdateArchitecture Current { get; } = arch;
    }

    private sealed class FakeFile(string name, byte[] bytes) : IDownloadedFile
    {
        private readonly MemoryStream _content = new(bytes);

        public string Path { get; } = name;

        public Stream Content => _content;

        public bool Kept { get; private set; }

        public bool Disposed { get; private set; }

        public void KeepOnDisk() => Kept = true;

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeDownloader : IUpdateDownloader
    {
        public List<FakeFile> Files { get; } = [];

        public List<(string Name, long Max)> Requests { get; } = [];

        public Func<ReleaseAsset, Task<FakeFile>>? Behavior { get; set; }

        public Task<IDownloadedFile> DownloadAsync(ReleaseAsset asset, long maxBytes, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
        {
            Requests.Add((asset.Name, maxBytes));
            return Task.Run<IDownloadedFile>(async () =>
            {
                var file = Behavior is null
                    ? new FakeFile(asset.Name, asset.Name.EndsWith(".sig", StringComparison.Ordinal) ? "sig"u8.ToArray() : [1, 2, 3])
                    : await Behavior(asset);
                Files.Add(file);
                return file;
            }, cancellationToken);
        }

        public void CleanUpStaleDownloads()
        {
        }
    }

    private sealed class FakeVerifier(bool result) : IUpdateSignatureVerifier
    {
        public string? SeenSignature { get; private set; }

        public Action? OnVerify { get; set; }

        public bool Verify(Stream content, string? signatureText)
        {
            SeenSignature = signatureText;
            OnVerify?.Invoke();
            return result;
        }
    }

    private sealed class FakeStarted(int exitCode, bool hang, Action? onWait) : IStartedInstaller
    {
        public bool Disposed { get; private set; }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            onWait?.Invoke();
            if (hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return exitCode;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeLauncher(bool starts = true, int exitCode = 0, bool hang = false) : IInstallerLauncher
    {
        public List<(string Path, InstallScope Scope)> Calls { get; } = [];

        /// <summary>Runs when the use case starts waiting for the installer (the file must still be held then).</summary>
        public Action? OnWait { get; set; }

        public FakeStarted? Started { get; private set; }

        public IStartedInstaller? Launch(string installerPath, InstallScope scope)
        {
            Calls.Add((installerPath, scope));
            return starts ? Started = new FakeStarted(exitCode, hang, OnWait) : null;
        }
    }

    private sealed class Fixture
    {
        public InstallInfo Install { get; set; } = new(InstallKind.Installed, InstallScope.CurrentUser);

        public bool LocationOk { get; set; } = true;

        public UpdateArchitecture Arch { get; set; } = UpdateArchitecture.X64;

        public bool Verified { get; set; } = true;

        public bool Launches { get; set; } = true;

        public Action? OnVerify { get; set; }

        public int ExitCode { get; set; }

        public bool Hang { get; set; }

        public FakeDownloader Downloader { get; } = new();

        public FakeLauncher Launcher { get; private set; } = new();

        public FakeVerifier Verifier { get; private set; } = new(true);

        public FakeLocation Location { get; private set; } = new(true);

        public InstallUpdateUseCase Build()
        {
            Launcher = new FakeLauncher(Launches, ExitCode, Hang);
            Verifier = new FakeVerifier(Verified) { OnVerify = OnVerify };
            Location = new FakeLocation(LocationOk);
            return new InstallUpdateUseCase(Current, new FakeInstallInfo(Install), Location, new FakeArch(Arch), Downloader, Verifier, Launcher);
        }
    }

    private static ReleaseInfo Release(AppVersion? version = null, bool draft = false, bool pre = false, params string[] names)
    {
        version ??= Newer;
        names = names.Length == 0
            ? [$"Snapdate-Setup-v{version}-win-x64.exe", $"Snapdate-Setup-v{version}-win-x64.exe.sig", $"Snapdate-Setup-v{version}-win-arm64.exe", $"Snapdate-Setup-v{version}-win-arm64.exe.sig"]
            : names;
        var assets = names.Select(n => new ReleaseAsset(n, new Uri($"https://github.com/jannikjordan/snapdate/releases/download/v{version}/{n}"), n.EndsWith(".sig", StringComparison.Ordinal) ? 89 : 1000)).ToList();
        return new ReleaseInfo(version, new Uri("https://github.com/jannikjordan/snapdate/releases/tag/v" + version), draft, pre, assets);
    }

    [Theory]
    [InlineData(InstallScope.CurrentUser)]
    [InlineData(InstallScope.AllUsers)]
    public async Task Valid_update_runs_the_installer_in_the_current_scope_and_completes_on_exit_code_0(InstallScope scope)
    {
        var f = new Fixture { Install = new(InstallKind.Installed, scope) };
        FakeFile? installerFile = null;
        var useCase = f.Build();
        f.Launcher.OnWait = () => installerFile = f.Downloader.Files[1];
        var disposedWhileWaiting = true;
        f.Launcher.OnWait += () => disposedWhileWaiting = f.Downloader.Files[1].Disposed;

        var result = await useCase.ExecuteAsync(Release(), null, null, CancellationToken.None);

        Assert.Equal(UpdateInstallResult.Completed, result);
        var call = Assert.Single(f.Launcher.Calls);
        Assert.Equal("Snapdate-Setup-v0.2.0-win-x64.exe", call.Path);
        Assert.Equal(scope, call.Scope);
        Assert.Equal("sig", f.Verifier.SeenSignature);
        Assert.Equal(scope, f.Location.Asked);
        Assert.NotNull(installerFile);
        Assert.False(disposedWhileWaiting, "the verified file must stay held while the installer runs");
        Assert.False(f.Downloader.Files[1].Kept); // the installer finished: nothing to keep
        Assert.True(f.Launcher.Started!.Disposed);
        Assert.All(f.Downloader.Files, file => Assert.True(file.Disposed));
        Assert.Contains(f.Downloader.Requests, r => r is { Name: "Snapdate-Setup-v0.2.0-win-x64.exe", Max: InstallUpdateUseCase.MaxInstallerBytes });
        Assert.Contains(f.Downloader.Requests, r => r is { Name: "Snapdate-Setup-v0.2.0-win-x64.exe.sig", Max: InstallUpdateUseCase.MaxSignatureBytes });
    }

    [Fact]
    public async Task Arm64_process_selects_the_arm64_installer()
    {
        var f = new Fixture { Arch = UpdateArchitecture.Arm64 };
        Assert.Equal(UpdateInstallResult.Completed, await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None));
        Assert.Equal("Snapdate-Setup-v0.2.0-win-arm64.exe", Assert.Single(f.Launcher.Calls).Path);
    }

    [Fact]
    public async Task Only_the_other_architecture_available_is_reported_and_nothing_is_downloaded()
    {
        var f = new Fixture { Arch = UpdateArchitecture.Arm64 };
        var release = Release(names: ["Snapdate-Setup-v0.2.0-win-x64.exe", "Snapdate-Setup-v0.2.0-win-x64.exe.sig"]);

        Assert.Equal(UpdateInstallResult.AssetMissing, await f.Build().ExecuteAsync(release, null, null, CancellationToken.None));
        Assert.Empty(f.Downloader.Requests);
        Assert.Empty(f.Launcher.Calls);
    }

    [Fact]
    public async Task Missing_signature_asset_is_reported()
    {
        var f = new Fixture();
        var release = Release(names: ["Snapdate-Setup-v0.2.0-win-x64.exe"]);
        Assert.Equal(UpdateInstallResult.AssetMissing, await f.Build().ExecuteAsync(release, null, null, CancellationToken.None));
        Assert.Empty(f.Downloader.Requests);
    }

    [Fact]
    public async Task Unsupported_architecture_is_not_applicable()
    {
        var f = new Fixture { Arch = UpdateArchitecture.Unsupported };
        Assert.Equal(UpdateInstallResult.NotApplicable, await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Portable_is_not_applicable()
    {
        var f = new Fixture { Install = InstallInfo.Portable };
        Assert.Equal(UpdateInstallResult.NotApplicable, await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None));
        Assert.Empty(f.Downloader.Requests);
    }

    [Fact]
    public async Task Marker_not_confirmed_by_the_registry_is_not_applicable()
    {
        var f = new Fixture { LocationOk = false };
        Assert.Equal(UpdateInstallResult.NotApplicable, await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None));
        Assert.Empty(f.Downloader.Requests);
        Assert.Empty(f.Launcher.Calls);
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.0.9")]
    public async Task Same_or_older_version_is_not_applicable(string version)
    {
        var f = new Fixture();
        var result = await f.Build().ExecuteAsync(Release(AppVersion.Parse(version)), null, null, CancellationToken.None);
        Assert.Equal(UpdateInstallResult.NotApplicable, result);
        Assert.Empty(f.Downloader.Requests);
    }

    [Fact]
    public async Task Draft_and_prerelease_are_not_applicable()
    {
        var f = new Fixture();
        var useCase = f.Build();
        Assert.Equal(UpdateInstallResult.NotApplicable, await useCase.ExecuteAsync(Release(draft: true), null, null, CancellationToken.None));
        Assert.Equal(UpdateInstallResult.NotApplicable, await useCase.ExecuteAsync(Release(pre: true), null, null, CancellationToken.None));
        Assert.Equal(UpdateInstallResult.NotApplicable, await useCase.ExecuteAsync(Release(AppVersion.Parse("0.2.0-rc.1")), null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Failed_verification_never_launches_and_removes_the_files()
    {
        var f = new Fixture { Verified = false };
        var result = await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None);

        Assert.Equal(UpdateInstallResult.VerificationFailed, result);
        Assert.Empty(f.Launcher.Calls);
        Assert.All(f.Downloader.Files, file => Assert.True(file.Disposed && !file.Kept));
    }

    [Fact]
    public async Task Download_failure_is_reported_without_exception()
    {
        var f = new Fixture();
        f.Downloader.Behavior = _ => throw new UpdateDownloadException("boom");
        Assert.Equal(UpdateInstallResult.DownloadFailed, await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None));
        Assert.Empty(f.Launcher.Calls);
    }

    [Fact]
    public async Task Declared_size_above_the_cap_fails_before_any_download()
    {
        var f = new Fixture();
        var release = Release();
        var big = release.Assets.Select(a => a.Name.EndsWith("x64.exe", StringComparison.Ordinal) ? a with { Size = InstallUpdateUseCase.MaxInstallerBytes + 1 } : a).ToList();
        var result = await f.Build().ExecuteAsync(release with { Assets = big }, null, null, CancellationToken.None);
        Assert.Equal(UpdateInstallResult.DownloadFailed, result);
        Assert.Empty(f.Downloader.Requests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Installer_exiting_non_zero_for_example_declined_uac_is_reported_and_files_are_removed(int exitCode)
    {
        var f = new Fixture { ExitCode = exitCode, Install = new(InstallKind.Installed, InstallScope.AllUsers) };
        var result = await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None);

        Assert.Equal(UpdateInstallResult.InstallerFailed, result);
        Assert.All(f.Downloader.Files, file => Assert.True(file.Disposed && !file.Kept));
    }

    [Fact]
    public async Task App_shutdown_while_waiting_keeps_the_files_and_reports_installer_running()
    {
        var f = new Fixture { Hang = true };
        using var shutdown = new CancellationTokenSource();
        var useCase = f.Build();
        f.Launcher.OnWait = () => shutdown.CancelAfter(50);

        var result = await useCase.ExecuteAsync(Release(), null, null, CancellationToken.None, shutdown.Token);

        Assert.Equal(UpdateInstallResult.InstallerRunning, result);
        Assert.True(f.Downloader.Files[1].Kept);
        Assert.True(f.Downloader.Files[1].Disposed);
    }

    [Fact]
    public async Task Cancelling_the_download_token_does_not_affect_a_started_installer()
    {
        var f = new Fixture { Hang = true };
        using var download = new CancellationTokenSource();
        using var shutdown = new CancellationTokenSource();
        var useCase = f.Build();
        f.Launcher.OnWait = () =>
        {
            download.Cancel();
            shutdown.CancelAfter(50);
        };

        var result = await useCase.ExecuteAsync(Release(), null, null, download.Token, shutdown.Token);
        Assert.Equal(UpdateInstallResult.InstallerRunning, result);
    }

    [Theory]
    [InlineData("v0.3.0")]
    [InlineData("0.2.0")]
    [InlineData("v0.1.0")]
    public async Task Assets_outside_the_releases_own_tag_folder_are_rejected(string tag)
    {
        var f = new Fixture();
        var release = Release();
        var moved = release.Assets
            .Select(a => a with { DownloadUrl = new Uri($"https://github.com/jannikjordan/snapdate/releases/download/{tag}/{a.Name}") })
            .ToList();

        Assert.Equal(UpdateInstallResult.AssetMissing, await f.Build().ExecuteAsync(release with { Assets = moved }, null, null, CancellationToken.None));
        Assert.Empty(f.Downloader.Requests);
    }

    [Fact]
    public async Task App_shutdown_during_verification_never_launches_and_removes_the_files()
    {
        using var shutdown = new CancellationTokenSource();
        var f = new Fixture { OnVerify = shutdown.Cancel };

        var result = await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None, shutdown.Token);

        Assert.Equal(UpdateInstallResult.Cancelled, result);
        Assert.Empty(f.Launcher.Calls);
        Assert.All(f.Downloader.Files, file => Assert.True(file.Disposed && !file.Kept));
    }

    [Fact]
    public async Task Download_cancellation_during_verification_never_launches_and_removes_the_files()
    {
        using var cancel = new CancellationTokenSource();
        var f = new Fixture { OnVerify = cancel.Cancel };

        var result = await f.Build().ExecuteAsync(Release(), null, null, cancel.Token);

        Assert.Equal(UpdateInstallResult.Cancelled, result);
        Assert.Empty(f.Launcher.Calls);
        Assert.All(f.Downloader.Files, file => Assert.True(file.Disposed && !file.Kept));
    }

    [Fact]
    public async Task Shutdown_before_verification_starts_skips_verification_and_launch()
    {
        using var shutdown = new CancellationTokenSource();
        var f = new Fixture();
        f.Downloader.Behavior = async asset =>
        {
            if (!asset.Name.EndsWith(".sig", StringComparison.Ordinal))
            {
                await shutdown.CancelAsync(); // the window starts closing right after the download finished
            }

            return new FakeFile(asset.Name, "sig"u8.ToArray());
        };

        var result = await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None, shutdown.Token);

        Assert.Equal(UpdateInstallResult.Cancelled, result);
        Assert.Null(f.Verifier.SeenSignature);
        Assert.Empty(f.Launcher.Calls);
    }

    [Fact]
    public async Task Launch_failure_removes_the_files()
    {
        var f = new Fixture { Launches = false };
        var result = await f.Build().ExecuteAsync(Release(), null, null, CancellationToken.None);
        Assert.Equal(UpdateInstallResult.LaunchFailed, result);
        Assert.All(f.Downloader.Files, file => Assert.False(file.Kept));
    }

    [Fact]
    public async Task Cancellation_is_reported_as_cancelled_and_nothing_is_launched()
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        f.Downloader.Behavior = async asset =>
        {
            if (!asset.Name.EndsWith(".sig", StringComparison.Ordinal))
            {
                await cts.CancelAsync();
                cts.Token.ThrowIfCancellationRequested();
            }

            return new FakeFile(asset.Name, "sig"u8.ToArray());
        };

        var result = await f.Build().ExecuteAsync(Release(), null, null, cts.Token);
        Assert.Equal(UpdateInstallResult.Cancelled, result);
        Assert.Empty(f.Launcher.Calls);
    }
}
