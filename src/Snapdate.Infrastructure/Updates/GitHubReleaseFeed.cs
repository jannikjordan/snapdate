using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Snapdate.Application.Ports;
using Snapdate.Application.Updates;
using Snapdate.Domain.Versioning;

namespace Snapdate.Infrastructure.Updates;

/// <summary>
/// Reads the latest release from the GitHub REST API. This is Snapdate's only network access for the
/// update check, and it is deliberately narrow: one fixed https URL on <c>api.github.com</c>, a
/// <c>User-Agent</c> of <c>Snapdate/&lt;version&gt;</c> and nothing else that identifies the user or machine,
/// a short timeout, a response size cap, and redirects only within the same host. See
/// docs/adr/0004-update-check-and-in-app-update.md.
/// </summary>
public sealed class GitHubReleaseFeed : IReleaseFeed, IDisposable
{
    /// <summary>The one and only URL that is ever requested (plus same-host redirects).</summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/jannikjordan/snapdate/releases/latest";

    internal const string ApiHost = "api.github.com";
    internal const string WebHost = "github.com";
    internal const string RepoPathPrefix = "/jannikjordan/snapdate/";
    internal const long MaxResponseBytes = 1024 * 1024;
    internal const int MaxRedirects = 3;
    internal const int MaxAssets = 50;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;
    private readonly string _userAgent;

    /// <summary>Production constructor: own HttpClient that never follows redirects by itself.</summary>
    public GitHubReleaseFeed(AppVersion currentVersion)
        : this(CreateHttpClient(), currentVersion, new Uri(LatestReleaseUrl), DefaultTimeout, ownsClient: true)
    {
    }

    internal GitHubReleaseFeed(HttpClient client, AppVersion currentVersion, Uri endpoint, TimeSpan timeout, bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!IsAllowedApiUri(endpoint))
        {
            throw new ArgumentException($"Only https://{ApiHost} is allowed.", nameof(endpoint));
        }

        _client = client;
        _ownsClient = ownsClient;
        _endpoint = endpoint;
        _timeout = timeout;
        _userAgent = "Snapdate/" + currentVersion;
    }

    /// <summary>Creates the HttpClient used in production: no automatic redirects, no cookies, no credentials.</summary>
    internal static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = DefaultTimeout,
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            var body = await FetchAsync(timeout.Token).ConfigureAwait(false);
            return body is null ? null : Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Offline, DNS, TLS, timeout, malformed answer ...: all mean "no information".
            return null;
        }
    }

    private async Task<byte[]?> FetchAsync(CancellationToken token)
    {
        var uri = _endpoint;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(_userAgent));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    return null;
                }

                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!IsAllowedApiUri(next))
                {
                    return null;
                }

                uri = next;
                continue;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null; // includes 403/429 (rate limit), 404, 5xx
            }

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                return null;
            }

            return await ReadCappedAsync(response.Content, token).ConfigureAwait(false);
        }

        return null; // too many redirects
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ReleaseInfo? Parse(byte[] body)
    {
        var dto = JsonSerializer.Deserialize(body, GitHubJsonContext.Default.GitHubReleaseDto);
        if (dto is null
            || !AppVersion.TryParse(dto.TagName, out var version)
            || !TryGetRepoUrl(dto.HtmlUrl, out var pageUrl))
        {
            return null;
        }

        var assets = new List<ReleaseAsset>();
        foreach (var a in dto.Assets ?? [])
        {
            if (assets.Count >= MaxAssets)
            {
                break;
            }

            if (IsSafeAssetName(a.Name) && a.Size >= 0 && TryGetAssetUrl(a.BrowserDownloadUrl, a.Name!, out var url))
            {
                assets.Add(new ReleaseAsset(a.Name!, url!, a.Size));
            }
        }

        return new ReleaseInfo(version!, pageUrl!, dto.Draft, dto.Prerelease, assets);
    }

    private static bool IsAllowedApiUri(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>Links we later show or download must point into our own repository on github.com over https.</summary>
    private static bool TryGetRepoUrl(string? text, out Uri? uri)
    {
        uri = null;
        if (text is null
            || text.Length > 2048
            || !Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !parsed.IsDefaultPort
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.Equals(parsed.Host, WebHost, StringComparison.OrdinalIgnoreCase)
            || !parsed.AbsolutePath.StartsWith(RepoPathPrefix, StringComparison.Ordinal)
            || HasDotSegment(parsed.AbsolutePath))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>True when any path segment is "." or ".." (also when percent-encoded).</summary>
    private static bool HasDotSegment(string path) =>
        path.Split('/').Select(Uri.UnescapeDataString).Any(s => s is "." or "..");

    /// <summary>Asset links must be exactly <c>/jannikjordan/snapdate/releases/download/&lt;tag&gt;/&lt;name&gt;</c>.</summary>
    private static bool TryGetAssetUrl(string? text, string name, out Uri? uri)
    {
        uri = null;
        if (!TryGetRepoUrl(text, out var parsed))
        {
            return false;
        }

        var segments = parsed!.AbsolutePath.Split('/');
        // "", owner, repo, "releases", "download", tag, name
        if (segments.Length != 7
            || segments[3] != "releases"
            || segments[4] != "download"
            || segments[5].Length == 0
            || segments[6] != name)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static bool IsSafeAssetName(string? name)
    {
        if (string.IsNullOrEmpty(name)
            || name.Length > 200
            || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+')
            || name.StartsWith('.')
            || name.EndsWith('.'))
        {
            return false;
        }

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? name : name[..dot];
        return !ReservedDeviceNames.Contains(stem);
    }
}
