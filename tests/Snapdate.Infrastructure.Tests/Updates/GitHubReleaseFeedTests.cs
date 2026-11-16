using System.Net;
using System.Text;
using Snapdate.Domain.Versioning;
using Snapdate.Infrastructure.Updates;

namespace Snapdate.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseFeedTests
{
    private const string ValidJson = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/jannikjordan/snapdate/releases/tag/v0.2.0",
          "draft": false,
          "prerelease": false,
          "unknown_field": { "x": 1 },
          "assets": [
            { "name": "Snapdate-Setup-v0.2.0-win-x64.exe", "size": 123456,
              "browser_download_url": "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/Snapdate-Setup-v0.2.0-win-x64.exe" },
            { "name": "Snapdate-Setup-v0.2.0-win-x64.exe.sig", "size": 96,
              "browser_download_url": "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/Snapdate-Setup-v0.2.0-win-x64.exe.sig" },
            { "name": "evil.exe", "size": 1, "browser_download_url": "https://evil.example/evil.exe" },
            { "name": "..\\x.exe", "size": 1, "browser_download_url": "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/x.exe" }
          ]
        }
        """;

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GitHubReleaseFeed Create(FakeHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), AppVersion.Parse("0.1.0"), new Uri(GitHubReleaseFeed.LatestReleaseUrl), timeout ?? TimeSpan.FromSeconds(10));

    private static FakeHandler Always(Func<HttpResponseMessage> make) => new((_, _) => Task.FromResult(make()));

    [Fact]
    public async Task Valid_payload_is_parsed()
    {
        var handler = Always(() => Json(ValidJson));
        var release = await Create(handler).GetLatestAsync(CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal(AppVersion.Parse("0.2.0"), release.Version);
        Assert.Equal("https://github.com/jannikjordan/snapdate/releases/tag/v0.2.0", release.PageUrl.AbsoluteUri);
        Assert.False(release.IsDraft);
        Assert.False(release.IsPreRelease);
        // Assets with foreign hosts or unsafe names are dropped.
        Assert.Equal(["Snapdate-Setup-v0.2.0-win-x64.exe", "Snapdate-Setup-v0.2.0-win-x64.exe.sig"], release.Assets.Select(a => a.Name));
        Assert.Equal(123456, release.Assets[0].Size);
        Assert.StartsWith("https://github.com/jannikjordan/snapdate/releases/download/", release.Assets[0].DownloadUrl.AbsoluteUri);
    }

    private const string Download = "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/";

    private static string ReleaseWithAsset(string name, string url) =>
        "{\"tag_name\":\"v0.2.0\",\"html_url\":\"https://github.com/jannikjordan/snapdate/releases/tag/v0.2.0\",\"assets\":[{\"name\":\""
        + name + "\",\"size\":1,\"browser_download_url\":\"" + url + "\"}]}";

    [Theory]
    [InlineData("CON")]
    [InlineData("con.exe")]
    [InlineData("NUL.txt")]
    [InlineData("Com1.sig")]
    [InlineData("LPT9")]
    [InlineData("aux.tar.gz")]
    [InlineData("name.")]
    [InlineData(".hidden")]
    public async Task Unsafe_asset_names_are_dropped(string name)
    {
        var release = await Create(Always(() => Json(ReleaseWithAsset(name, Download + name)))).GetLatestAsync(CancellationToken.None);
        Assert.Empty(release!.Assets);
    }

    [Fact]
    public async Task Names_that_merely_contain_a_device_name_are_kept()
    {
        var release = await Create(Always(() => Json(ReleaseWithAsset("console-v1.exe", Download + "console-v1.exe")))).GetLatestAsync(CancellationToken.None);
        Assert.Single(release!.Assets);
    }

    [Theory]
    [InlineData("a.exe", "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/b.exe")]
    [InlineData("a.exe", "https://github.com/jannikjordan/snapdate/releases/latest/download/a.exe")]
    [InlineData("a.exe", "https://github.com/jannikjordan/snapdate/raw/main/a.exe")]
    [InlineData("a.exe", "https://github.com/jannikjordan/snapdate/releases/download/a.exe")]
    [InlineData("a.exe", "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/x/a.exe")]
    [InlineData("a.exe", "https://github.com/jannikjordan/snapdate/releases/download/v0.2.0/%2e%2e/a.exe")]
    public async Task Asset_urls_must_match_the_release_download_pattern_and_name(string name, string url)
    {
        var release = await Create(Always(() => Json(ReleaseWithAsset(name, url)))).GetLatestAsync(CancellationToken.None);
        Assert.Empty(release!.Assets);
    }

    [Fact]
    public async Task Dots_inside_a_path_segment_are_fine_but_dot_segments_are_rejected()
    {
        var ok = "{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://github.com/jannikjordan/snapdate/releases/tag/a..b\"}";
        Assert.NotNull(await Create(Always(() => Json(ok))).GetLatestAsync(CancellationToken.None));

        var bad = "{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://github.com/jannikjordan/snapdate/releases/%2e%2e/%2e%2e/x\"}";
        Assert.Null(await Create(Always(() => Json(bad))).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Request_is_a_plain_GET_to_the_fixed_endpoint_with_only_a_versioned_user_agent()
    {
        var handler = Always(() => Json(ValidJson));
        await Create(handler).GetLatestAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(GitHubReleaseFeed.LatestReleaseUrl, request.RequestUri!.AbsoluteUri);
        Assert.Equal("Snapdate/0.1.0", request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Equal(["Accept", "User-Agent"], request.Headers.Select(h => h.Key).Order());
    }

    [Fact]
    public async Task Draft_and_prerelease_flags_are_passed_through_for_the_service_to_judge()
    {
        var json = ValidJson.Replace("\"draft\": false", "\"draft\": true", StringComparison.Ordinal)
            .Replace("\"prerelease\": false", "\"prerelease\": true", StringComparison.Ordinal);
        var release = await Create(Always(() => Json(json))).GetLatestAsync(CancellationToken.None);
        Assert.True(release!.IsDraft);
        Assert.True(release.IsPreRelease);
    }

    [Theory]
    [InlineData("http://api.github.com/repos/jannikjordan/snapdate/releases/latest")]
    [InlineData("https://example.com/repos/jannikjordan/snapdate/releases/latest")]
    [InlineData("https://api.github.com.evil.example/x")]
    [InlineData("https://user@api.github.com/x")]
    [InlineData("https://api.github.com:8443/x")]
    public void Endpoint_other_than_https_api_github_com_is_rejected(string url)
    {
        Assert.Throws<ArgumentException>(() =>
            new GitHubReleaseFeed(new HttpClient(Always(() => Json("{}"))), AppVersion.Parse("0.1.0"), new Uri(url), TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData("https://evil.example/latest")]
    [InlineData("http://api.github.com/repos/x")]
    [InlineData("https://github.com/jannikjordan/snapdate/releases/latest")]
    [InlineData("https://api.github.com.evil.example/x")]
    public async Task Redirect_to_another_host_or_scheme_is_rejected_and_not_followed(string target)
    {
        var handler = new FakeHandler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri(target);
            return Task.FromResult(r);
        });

        Assert.Null(await Create(handler).GetLatestAsync(CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Redirect_within_api_github_com_is_followed_a_limited_number_of_times()
    {
        var handler = new FakeHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/repositories/42/releases/latest")
            {
                return Task.FromResult(Json(ValidJson));
            }

            var r = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            r.Headers.Location = new Uri("/repositories/42/releases/latest", UriKind.Relative);
            return Task.FromResult(r);
        });
        Assert.NotNull(await Create(handler).GetLatestAsync(CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);

        var loop = new FakeHandler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://api.github.com/loop");
            return Task.FromResult(r);
        });
        Assert.Null(await Create(loop).GetLatestAsync(CancellationToken.None));
        Assert.Equal(GitHubReleaseFeed.MaxRedirects + 1, loop.Requests.Count);
    }

    [Fact]
    public async Task Oversized_body_is_rejected_without_content_length()
    {
        var huge = new string('x', (int)GitHubReleaseFeed.MaxResponseBytes + 10);
        var json = "{\"tag_name\":\"v0.2.0\",\"html_url\":\"https://github.com/jannikjordan/snapdate/releases/tag/v0.2.0\",\"pad\":\"" + huge + "\"}";
        var handler = Always(() =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(json))) };
            r.Content.Headers.ContentLength = null;
            return r;
        });
        Assert.Null(await Create(handler).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Oversized_declared_content_length_is_rejected()
    {
        var handler = Always(() =>
        {
            var r = Json(ValidJson);
            r.Content.Headers.ContentLength = GitHubReleaseFeed.MaxResponseBytes + 1;
            return r;
        });
        Assert.Null(await Create(handler).GetLatestAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Error_statuses_yield_no_information(HttpStatusCode status)
    {
        Assert.Null(await Create(Always(() => Json(ValidJson, status))).GetLatestAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"latest\",\"html_url\":\"https://github.com/jannikjordan/snapdate/releases/tag/x\"}")]
    [InlineData("{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://evil.example/jannikjordan/snapdate/\"}")]
    [InlineData("{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://github.com/someoneelse/repo/releases/tag/v1.0.0\"}")]
    [InlineData("{\"tag_name\":\"v1.0.0\",\"html_url\":\"https://github.com/jannikjordan/snapdate/releases/tag/v1.0.0\",\"draft\":\"yes\"}")]
    public async Task Malformed_or_untrustworthy_payload_yields_no_information(string body)
    {
        Assert.Null(await Create(Always(() => Json(body))).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Network_failure_yields_no_information()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("offline"));
        Assert.Null(await Create(handler).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_yields_no_information()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(ValidJson);
        });
        Assert.Null(await Create(handler, TimeSpan.FromMilliseconds(50)).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(ValidJson);
        });
        using var cts = new CancellationTokenSource();
        var task = Create(handler).GetLatestAsync(cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void Production_client_does_not_follow_redirects_automatically()
    {
        using var feed = new GitHubReleaseFeed(AppVersion.Parse("0.1.0"));
        using var client = GitHubReleaseFeed.CreateHttpClient();
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}
