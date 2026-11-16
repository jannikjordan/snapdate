using System.Net;
using Snapdate.Application.Ports;
using Snapdate.Application.Updates;
using Snapdate.Domain.Versioning;
using Snapdate.Infrastructure.Tests.Support;
using Snapdate.Infrastructure.Updates;

namespace Snapdate.Infrastructure.Tests.Updates;

public sealed class HttpUpdateDownloaderTests : IDisposable
{
    private const string Name = "Snapdate-Setup-v0.2.0-win-x64.exe";
    private const string StartUrl = "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/" + Name;
    private const string CdnUrl = "https://release-assets.githubusercontent.com/github-production-release-asset/1/abc?token=1";

    private static readonly byte[] Payload = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

    private readonly TempFolder _temp = new();
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<HttpUpdateDownloader> _downloaders = [];
    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        foreach (var d in _downloaders)
        {
            d.Dispose();
        }

        foreach (var junction in _junctions)
        {
            Directory.Delete(junction, recursive: false); // removes only the link
        }

        _temp.Dispose();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<HttpRequestMessage> log) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            log.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A non-seekable body: gives <paramref name="data"/> and then ends or hangs until cancelled.</summary>
    private sealed class ScriptedStream(byte[] data, bool thenHang) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < data.Length)
            {
                var n = Math.Min(Math.Min(buffer.Length, data.Length - _position), 1000);
                data.AsMemory(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            if (thenHang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private HttpUpdateDownloader Create(Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan? idle = null, TimeSpan? stale = null, TimeSpan? overall = null, string? root = null)
    {
        var client = new HttpClient(new FakeHandler(respond, _requests));
        var d = new HttpUpdateDownloader(client, AppVersion.Parse("0.1.0"), root ?? _temp.Path, idle ?? TimeSpan.FromSeconds(5), overall ?? TimeSpan.FromMinutes(1), stale ?? TimeSpan.FromHours(1));
        _downloaders.Add(d);
        return d;
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Redirect(string to, HttpStatusCode code = HttpStatusCode.Found)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return r;
    }

    private static HttpResponseMessage Streamed(byte[] body, bool hang = false) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new ScriptedStream(body, hang)) };

    private static ReleaseAsset Asset(string name = Name, string? url = null, long? size = null) =>
        new(name, new Uri(url ?? "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/" + name), size ?? Payload.Length);

    private static HttpResponseMessage GitHubFlow(HttpRequestMessage r) =>
        r.RequestUri!.Host == "github.com" ? Redirect(CdnUrl) : Ok(Payload);

    private void AssertNothingLeft() => Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path));

    [Fact]
    public async Task Downloads_through_the_cdn_redirect_into_a_fresh_subfolder_and_reports_progress()
    {
        var reports = new List<UpdateProgress>();
        using var file = await Create(GitHubFlow).DownloadAsync(Asset(), 1_000_000, new Progress<UpdateProgress>(reports.Add), TestContext.Current.CancellationToken);

        var folder = Path.GetDirectoryName(file.Path)!;
        Assert.Equal(_temp.Path, Path.GetDirectoryName(folder));
        Assert.Equal(32, Path.GetFileName(folder).Length);
        Assert.Equal(Name, Path.GetFileName(file.Path));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(file.Path, TestContext.Current.CancellationToken));

        var content = new MemoryStream();
        await file.Content.CopyToAsync(content, TestContext.Current.CancellationToken);
        Assert.Equal(Payload, content.ToArray());
        Assert.Equal(2, _requests.Count);
        Assert.Equal("release-assets.githubusercontent.com", _requests[1].RequestUri!.Host);
    }

    [Fact]
    public async Task Requests_carry_only_user_agent_and_accept()
    {
        using var file = await Create(GitHubFlow).DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);

        Assert.All(_requests, r =>
        {
            Assert.Equal("Snapdate/0.1.0", r.Headers.UserAgent.ToString());
            Assert.Null(r.Headers.Authorization);
            Assert.False(r.Headers.Contains("Cookie"));
            Assert.False(r.Headers.Contains("Referer"));
        });
    }

    [Fact]
    public async Task Held_handle_denies_writers_and_deleters_until_disposed()
    {
        var file = await Create(r => Ok(Payload)).DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);

        Assert.Throws<IOException>(() => new FileStream(file.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
        Assert.Throws<IOException>(() => File.Delete(file.Path));
        using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
        }

        file.Dispose();
        AssertNothingLeft();
    }

    [Fact]
    public async Task KeepOnDisk_leaves_the_files_after_dispose()
    {
        var file = await Create(r => Ok(Payload)).DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);
        file.KeepOnDisk();
        file.Dispose();
        Assert.True(File.Exists(file.Path));
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("https://github.com.evil.example/x")]
    [InlineData("http://release-assets.githubusercontent.com/x")]
    [InlineData("https://release-assets.githubusercontent.com:8443/x")]
    [InlineData("https://user:pw@objects.githubusercontent.com/x")]
    [InlineData("https://api.github.com/x")]
    [InlineData("ftp://github.com/x")]
    public async Task Redirect_to_a_host_scheme_or_port_that_is_not_allowed_is_rejected(string target)
    {
        var d = Create(r => r.RequestUri!.Host == "github.com" ? Redirect(target) : Ok(Payload));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        Assert.Single(_requests); // the forbidden hop was never requested
        AssertNothingLeft();
    }

    [Fact]
    public async Task Too_many_redirects_are_rejected()
    {
        var d = Create(r => Redirect("https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/" + Name + "?x=" + _requests.Count));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        Assert.Equal(HttpUpdateDownloader.MaxRedirects + 1, _requests.Count);
        AssertNothingLeft();
    }

    [Fact]
    public async Task Relative_redirect_stays_on_the_allowed_host()
    {
        var d = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/moved", StringComparison.Ordinal) ? Ok(Payload) : Redirect("/jannikjordan/snapdate/releases/download/v0.2.0/moved"));
        using var file = await d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(file.Path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://github.com/jannikjordan/snapdate/releases/download/v0.2.0/" + Name)]
    [InlineData("https://github.com/jannikjordan/other/releases/download/v0.2.0/" + Name)]
    [InlineData("https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/other.exe")]
    [InlineData("https://github.com/jannikjordan/snapdate/releases/download/" + Name)]
    [InlineData("https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/" + Name + "?a=b")]
    [InlineData("https://github.com/jannikjordan/snapdate/releases/download/../" + Name)]
    [InlineData("https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/x/" + Name)]
    [InlineData("https://objects.githubusercontent.com/jannikjordan/snapdate/releases/download/v0.2.0/" + Name)]
    [InlineData("https://github.com:444/jannikjordan/snapdate/releases/download/v0.2.0/" + Name)]
    public async Task Start_url_must_be_the_exact_asset_shape(string url)
    {
        var d = Create(r => Ok(Payload));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(url: url), 1_000_000, null, TestContext.Current.CancellationToken));
        Assert.Empty(_requests);
        AssertNothingLeft();
    }

    [Theory]
    [InlineData("..\\evil.exe")]
    [InlineData("../evil.exe")]
    [InlineData("a/b.exe")]
    [InlineData("a\\b.exe")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:evil.exe")]
    [InlineData("con.exe")]
    [InlineData("name with space.exe")]
    [InlineData("trailing.")]
    [InlineData("")]
    public async Task Asset_names_that_could_escape_the_folder_are_rejected(string name)
    {
        var d = Create(r => Ok(Payload));
        var asset = new ReleaseAsset(name, new Uri(StartUrl), Payload.Length);
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(asset, 1_000_000, null, TestContext.Current.CancellationToken));
        Assert.Empty(_requests);
        AssertNothingLeft();
    }

    [Fact]
    public async Task Truncated_download_is_rejected_and_removed()
    {
        var d = Create(r => Streamed(Payload[..3000]));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Fact]
    public async Task More_bytes_than_declared_is_rejected_even_without_content_length()
    {
        var d = Create(r => Streamed([.. Payload, .. new byte[500]]));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Fact]
    public async Task Content_length_that_differs_from_the_declared_size_is_rejected()
    {
        var d = Create(r => Ok([.. Payload, .. new byte[10]]));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Fact]
    public async Task Declared_size_over_the_cap_is_rejected_without_a_request()
    {
        var d = Create(r => Ok(Payload));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), Payload.Length - 1, null, TestContext.Current.CancellationToken));
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Oversized_body_is_cut_off_at_the_declared_size()
    {
        // The server claims the declared size in the feed but streams far more.
        var d = Create(r => Streamed(new byte[200_000]));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(size: 1000), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.PartialContent)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Non_200_status_is_a_failure(HttpStatusCode code)
    {
        var d = Create(r => new HttpResponseMessage(code) { Content = new ByteArrayContent(Payload) });
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Fact]
    public async Task Content_encoding_is_rejected()
    {
        var d = Create(r =>
        {
            var response = Ok(Payload);
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        });
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Fact]
    public async Task Network_error_is_wrapped()
    {
        var d = Create(r => throw new HttpRequestException("offline"));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Fact]
    public async Task A_stalled_connection_times_out()
    {
        var d = Create(r => Streamed(Payload[..1000], hang: true), idle: TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        AssertNothingLeft();
    }

    [Fact]
    public async Task Cancellation_mid_download_cleans_up_and_surfaces_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var d = Create(r => Streamed(Payload[..2000], hang: true));
        var progress = new Progress<UpdateProgress>(p =>
        {
            if (p.BytesReceived > 0)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Progress callbacks are posted to the thread pool and can arrive after the test ended.
                }
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => d.DownloadAsync(Asset(), 1_000_000, progress, cts.Token));
        AssertNothingLeft();
    }

    private void MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(link);
    }

    [Fact]
    public async Task Junction_at_the_update_root_is_refused_and_nothing_lands_in_its_target()
    {
        var target = _temp.Combine("elsewhere");
        Directory.CreateDirectory(target);
        var root = _temp.Combine("updates");
        MakeJunction(root, target);

        var d = Create(r => Ok(Payload), root: root);
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public async Task Junction_at_the_parent_of_the_update_root_is_refused()
    {
        var target = _temp.Combine("elsewhere");
        Directory.CreateDirectory(target);
        var parent = _temp.Combine("Snapdate");
        MakeJunction(parent, target);

        var d = Create(r => Ok(Payload), root: Path.Combine(parent, "updates"));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)); // at most an empty "updates" folder
    }

    [Fact]
    public void Final_path_check_detects_a_file_reached_through_a_junction()
    {
        var real = _temp.Combine("real");
        Directory.CreateDirectory(real);
        var file = _temp.Write(Path.Combine("real", "a.bin"), [1, 2, 3]);
        var link = _temp.Combine("link");
        MakeJunction(link, real);

        using var direct = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.True(NativePaths.IsSameLocation(direct, file));

        using var viaJunction = new FileStream(Path.Combine(link, "a.bin"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.False(NativePaths.IsSameLocation(viaJunction, Path.Combine(link, "a.bin")));
    }

    [Theory]
    [InlineData(@"\\?\C:\Users\x\a.exe", @"C:\Users\x\a.exe")]
    [InlineData(@"\\?\UNC\server\share\a.exe", @"\\server\share\a.exe")]
    [InlineData(@"C:\plain\a.exe", @"C:\plain\a.exe")]
    public void Final_path_prefixes_are_stripped(string input, string expected)
    {
        Assert.Equal(expected, NativePaths.StripPrefix(input));
    }

    [Fact]
    public async Task File_and_folder_cannot_be_renamed_while_held()
    {
        using var file = await Create(r => Ok(Payload)).DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);
        var folder = Path.GetDirectoryName(file.Path)!;

        Assert.ThrowsAny<Exception>(() => File.Move(file.Path, file.Path + ".moved"));
        Assert.ThrowsAny<Exception>(() => Directory.Move(folder, folder + "-moved"));
        Assert.True(File.Exists(file.Path));
    }

    [Fact]
    public async Task Protocol_relative_redirect_to_another_host_is_rejected()
    {
        var d = Create(r => Redirect("//evil.example/x"));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        Assert.Single(_requests);
    }

    [Theory]
    [InlineData("https://github.com./x")]
    [InlineData("https://GITHUB.COM.evil.example/x")]
    [InlineData("https://release-assets.githubusercontent.com./x")]
    public async Task Trailing_dot_hosts_are_rejected(string target)
    {
        var d = Create(r => r.RequestUri!.Host == "github.com" ? Redirect(target) : Ok(Payload));
        await Assert.ThrowsAsync<UpdateDownloadException>(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        Assert.Single(_requests);
    }

    [Fact]
    public async Task Uppercase_host_and_scheme_in_redirects_are_accepted_as_the_same_allowed_host()
    {
        var d = Create(r => r.RequestUri!.Host == "github.com" ? Redirect("HTTPS://RELEASE-ASSETS.GITHUBUSERCONTENT.COM/x") : Ok(Payload));
        using var file = await d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);
        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task Overall_timeout_is_a_download_failure_not_a_cancellation()
    {
        var d = Create(r => Streamed(Payload[..1000], hang: true), idle: TimeSpan.FromMinutes(5), overall: TimeSpan.FromMilliseconds(300));
        var ex = await Record.ExceptionAsync(() => d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken));
        Assert.IsType<UpdateDownloadException>(ex);
        AssertNothingLeft();
    }

    [Fact]
    public void Stale_cleanup_does_not_follow_a_junction_folder()
    {
        var outside = _temp.Combine("outside");
        var precious = _temp.Write(Path.Combine("outside", "keep.txt"), [1], DateTime.UtcNow.AddHours(-5));
        Directory.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddHours(-5));
        var root = _temp.Combine("updates");
        Directory.CreateDirectory(root);
        MakeJunction(Path.Combine(root, new string('d', 32)), outside);

        Create(r => Ok(Payload), root: root).CleanUpStaleDownloads();

        Assert.True(File.Exists(precious));
    }

    [Fact]
    public void Stale_cleanup_does_nothing_when_the_root_is_a_junction()
    {
        var target = _temp.Combine("elsewhere");
        var victim = _temp.Write(Path.Combine("elsewhere", new string('e', 32), "x.bin"), [1], DateTime.UtcNow.AddHours(-5));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(victim)!, DateTime.UtcNow.AddHours(-5));
        var root = _temp.Combine("updates");
        MakeJunction(root, target);

        Create(r => Ok(Payload), root: root).CleanUpStaleDownloads();

        Assert.True(File.Exists(victim));
    }

    [Fact]
    public async Task Each_download_uses_its_own_fresh_folder()
    {
        var d = Create(r => Ok(Payload));
        using var a = await d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);
        using var b = await d.DownloadAsync(Asset(), 1_000_000, null, TestContext.Current.CancellationToken);
        Assert.NotEqual(Path.GetDirectoryName(a.Path), Path.GetDirectoryName(b.Path));
    }

    [Fact]
    public void Stale_cleanup_removes_only_old_folders_of_our_own_shape()
    {
        var old = _temp.Write(Path.Combine(new string('a', 32), "Setup.exe"), [1], DateTime.UtcNow.AddHours(-5));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(old)!, DateTime.UtcNow.AddHours(-5));
        var fresh = _temp.Write(Path.Combine(new string('b', 32), "Setup.exe"), [1]);
        var foreign = _temp.Write(Path.Combine("my-photos", "x.jpg"), [1], DateTime.UtcNow.AddHours(-5));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(foreign)!, DateTime.UtcNow.AddHours(-5));
        var looseFile = _temp.Write("loose.txt", [1], DateTime.UtcNow.AddHours(-5));

        Create(r => Ok(Payload)).CleanUpStaleDownloads();

        Assert.False(Directory.Exists(Path.GetDirectoryName(old)!));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(foreign));
        Assert.True(File.Exists(looseFile));
    }

    [Fact]
    public void Stale_cleanup_survives_a_locked_folder_and_a_missing_root()
    {
        var path = _temp.Write(Path.Combine(new string('c', 32), "Setup.exe"), [1], DateTime.UtcNow.AddHours(-5));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(path)!, DateTime.UtcNow.AddHours(-5));
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Create(r => Ok(Payload)).CleanUpStaleDownloads(); // must not throw
        }

        var client = new HttpClient(new FakeHandler(r => Ok(Payload), _requests));
        new HttpUpdateDownloader(client, AppVersion.Parse("0.1.0"), _temp.Combine("missing"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.Zero).CleanUpStaleDownloads();
    }
}
