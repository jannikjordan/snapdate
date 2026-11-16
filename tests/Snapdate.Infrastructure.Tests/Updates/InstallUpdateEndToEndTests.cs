using System.Net;
using System.Security.Cryptography;
using Snapdate.Application.Installation;
using Snapdate.Application.Ports;
using Snapdate.Application.Updates;
using Snapdate.Domain.Versioning;
using Snapdate.Infrastructure.Tests.Support;
using Snapdate.Infrastructure.Updates;

namespace Snapdate.Infrastructure.Tests.Updates;

/// <summary>The use case with the real downloader (fake HTTP) and the real verifier (throwaway key).</summary>
public sealed class InstallUpdateEndToEndTests : IDisposable
{
    private const string Installer = "Snapdate-Setup-v0.2.0-win-x64.exe";
    private const string Sig = Installer + ".sig";

    private readonly TempFolder _temp = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _installerBytes = RandomNumberGenerator.GetBytes(20_000);
    private readonly Dictionary<string, byte[]> _served = [];
    private readonly List<(string Path, InstallScope Scope, bool WasOpenForRead, byte[] Bytes)> _launches = [];

    public InstallUpdateEndToEndTests()
    {
        _served[Installer] = _installerBytes;
        _served[Sig] = SigText(_key, _installerBytes);
    }

    public void Dispose()
    {
        _key.Dispose();
        _otherKey.Dispose();
        _temp.Dispose();
    }

    private sealed class FakeInstallInfo(InstallInfo info) : IInstallInfo
    {
        public InstallInfo Current { get; } = info;
    }

    private sealed class FakeLocation(bool ok) : IInstallLocationCheck
    {
        public bool IsRegisteredForRunningExe(InstallScope scope) => ok;
    }

    private sealed class FakeArch : IProcessArchitecture
    {
        public UpdateArchitecture Current => UpdateArchitecture.X64;
    }

    private sealed class Exited : IStartedInstaller
    {
        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public void Dispose()
        {
        }
    }

    private sealed class Launcher(List<(string Path, InstallScope Scope, bool WasOpenForRead, byte[] Bytes)> log) : IInstallerLauncher
    {
        public IStartedInstaller? Launch(string installerPath, InstallScope scope)
        {
            // While launching, the verified file must still be locked against writers (the TOCTOU guard).
            var locked = false;
            try
            {
                using var _ = new FileStream(installerPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            }
            catch (IOException)
            {
                locked = true;
            }

            log.Add((installerPath, scope, locked, File.ReadAllBytes(installerPath)));
            return new Exited();
        }
    }

    private sealed class FakeHandler(Func<string, byte[]?> serve) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.Segments[^1];
            var body = serve(name);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private static byte[] SigText(ECDsa key, byte[] data) =>
        System.Text.Encoding.ASCII.GetBytes(
            Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) + "\n");

    private ReleaseInfo Release(long? installerSize = null, long? sigSize = null)
    {
        ReleaseAsset Asset(string name, long size) =>
            new(name, new Uri("https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/" + name), size);

        return new ReleaseInfo(
            AppVersion.Parse("0.2.0"),
            new Uri("https://github.com/jannikjordan/snapdate/releases/tag/v0.2.0"),
            false,
            false,
            [Asset(Installer, installerSize ?? _served[Installer].Length), Asset(Sig, sigSize ?? _served[Sig].Length)]);
    }

    private async Task<UpdateInstallResult> RunAsync(ReleaseInfo release, CancellationToken token, InstallScope scope = InstallScope.CurrentUser, bool registered = true)
    {
        using var verifier = new EcdsaUpdateSignatureVerifier([_key.ExportSubjectPublicKeyInfoPem()]);
        using var downloader = new HttpUpdateDownloader(
            new HttpClient(new FakeHandler(n => _served.GetValueOrDefault(n))),
            AppVersion.Parse("0.1.0"),
            _temp.Path,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1));
        var useCase = new InstallUpdateUseCase(
            AppVersion.Parse("0.1.0"),
            new FakeInstallInfo(new InstallInfo(InstallKind.Installed, scope)),
            new FakeLocation(registered),
            new FakeArch(),
            downloader,
            verifier,
            new Launcher(_launches));
        return await useCase.ExecuteAsync(release, null, token);
    }

    private void AssertCleanAndNotLaunched()
    {
        Assert.Empty(_launches);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path));
    }

    [Theory]
    [InlineData(InstallScope.CurrentUser)]
    [InlineData(InstallScope.AllUsers)]
    public async Task Valid_download_and_signature_start_the_installer_with_the_file_locked(InstallScope scope)
    {
        var result = await RunAsync(Release(), TestContext.Current.CancellationToken, scope);

        Assert.Equal(UpdateInstallResult.Completed, result);
        var launch = Assert.Single(_launches);
        Assert.Equal(scope, launch.Scope);
        Assert.True(launch.WasOpenForRead, "the verified file must be write-locked during launch");
        Assert.Equal(_installerBytes, launch.Bytes);

        // The installer finished (exit code 0): nothing is left behind.
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path));
    }

    [Fact]
    public async Task Tampered_installer_is_rejected_deleted_and_not_launched()
    {
        var tampered = (byte[])_installerBytes.Clone();
        tampered[100] ^= 0xFF;
        _served[Installer] = tampered;

        Assert.Equal(UpdateInstallResult.VerificationFailed, await RunAsync(Release(), token: TestContext.Current.CancellationToken));
        AssertCleanAndNotLaunched();
    }

    [Fact]
    public async Task Signature_by_another_key_is_rejected()
    {
        _served[Sig] = SigText(_otherKey, _installerBytes);

        Assert.Equal(UpdateInstallResult.VerificationFailed, await RunAsync(Release(), token: TestContext.Current.CancellationToken));
        AssertCleanAndNotLaunched();
    }

    [Theory]
    [InlineData("not base64 at all\n")]
    [InlineData("AAAA\n")]
    [InlineData("")]
    public async Task Malformed_signature_is_rejected(string text)
    {
        _served[Sig] = System.Text.Encoding.ASCII.GetBytes(text);

        var result = await RunAsync(Release(sigSize: Math.Max(1, _served[Sig].Length)), token: TestContext.Current.CancellationToken);

        Assert.True(result is UpdateInstallResult.VerificationFailed or UpdateInstallResult.DownloadFailed);
        AssertCleanAndNotLaunched();
    }

    [Fact]
    public async Task Oversized_signature_is_rejected_before_any_download()
    {
        _served[Sig] = new byte[5000];
        Assert.Equal(UpdateInstallResult.DownloadFailed, await RunAsync(Release(), token: TestContext.Current.CancellationToken));
        AssertCleanAndNotLaunched();
    }

    [Fact]
    public async Task Truncated_installer_is_a_download_failure()
    {
        _served[Installer] = _installerBytes[..10_000];
        Assert.Equal(UpdateInstallResult.DownloadFailed, await RunAsync(Release(installerSize: _installerBytes.Length), token: TestContext.Current.CancellationToken));
        AssertCleanAndNotLaunched();
    }

    [Fact]
    public async Task Size_differing_from_the_declared_size_is_a_download_failure()
    {
        Assert.Equal(UpdateInstallResult.DownloadFailed, await RunAsync(Release(installerSize: _installerBytes.Length - 1), token: TestContext.Current.CancellationToken));
        AssertCleanAndNotLaunched();
    }

    [Fact]
    public async Task Installer_over_the_hard_cap_is_a_download_failure()
    {
        Assert.Equal(UpdateInstallResult.DownloadFailed, await RunAsync(Release(installerSize: InstallUpdateUseCase.MaxInstallerBytes + 1), token: TestContext.Current.CancellationToken));
        AssertCleanAndNotLaunched();
    }

    [Fact]
    public async Task Marker_without_matching_registry_entry_is_not_applicable()
    {
        Assert.Equal(UpdateInstallResult.NotApplicable, await RunAsync(Release(), registered: false, token: TestContext.Current.CancellationToken));
        AssertCleanAndNotLaunched();
    }

    [Fact]
    public async Task Cancelled_before_start_is_cancelled_and_clean()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        Assert.Equal(UpdateInstallResult.Cancelled, await RunAsync(Release(), token: cts.Token));
        AssertCleanAndNotLaunched();
    }
}
