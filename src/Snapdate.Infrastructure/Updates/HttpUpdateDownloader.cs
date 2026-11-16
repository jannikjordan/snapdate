using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using Snapdate.Application.Ports;
using Snapdate.Application.Updates;
using Snapdate.Domain.Paths;
using Snapdate.Domain.Versioning;

namespace Snapdate.Infrastructure.Updates;

/// <summary>
/// Downloads release files from our own GitHub repository (ADR 0004, updater section). The start URL must be
/// exactly <c>https://github.com/jannikjordan/snapdate/releases/download/&lt;tag&gt;/&lt;name&gt;</c>; redirects are followed
/// by hand, at most <see cref="MaxRedirects"/>, https on the default port only, and each hop must be on
/// <see cref="AllowedHosts"/>. Size is enforced on the bytes actually received. The file lands in a fresh random
/// folder below <c>%LOCALAPPDATA%\Snapdate\updates</c>; on any failure the folder is removed.
/// </summary>
public sealed class HttpUpdateDownloader : IUpdateDownloader, IDisposable
{
    internal const int MaxRedirects = 5;
    internal const string RepoPathPrefix = "/jannikjordan/snapdate/releases/download/";

    /// <summary>The only hosts a request (start URL or redirect hop) may go to.</summary>
    internal static readonly FrozenSet<string> AllowedHosts = new[]
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultStaleAge = TimeSpan.FromHours(1);

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _root;
    private readonly string _userAgent;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _overallTimeout;
    private readonly TimeSpan _staleAge;

    /// <summary>Production constructor: no automatic redirects, cookies, credentials or decompression.</summary>
    public HttpUpdateDownloader(AppVersion currentVersion)
        : this(CreateHttpClient(), currentVersion, DefaultRoot(), DefaultIdleTimeout, DefaultOverallTimeout, DefaultStaleAge, ownsClient: true)
    {
    }

    internal HttpUpdateDownloader(
        HttpClient client,
        AppVersion currentVersion,
        string root,
        TimeSpan idleTimeout,
        TimeSpan overallTimeout,
        TimeSpan staleAge,
        bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _client = client;
        _ownsClient = ownsClient;
        _root = root;
        _userAgent = "Snapdate/" + currentVersion;
        _idleTimeout = idleTimeout;
        _overallTimeout = overallTimeout;
        _staleAge = staleAge;
    }

    internal static string DefaultRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(local) ? string.Empty : Path.Combine(local, "Snapdate", "updates");
    }

    internal static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = DefaultConnectTimeout,
            PreAuthenticate = false,
            Credentials = null,
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

    public async Task<IDownloadedFile> DownloadAsync(
        ReleaseAsset asset,
        long maxBytes,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (string.IsNullOrEmpty(_root))
        {
            throw new UpdateDownloadException("No local application data folder.");
        }

        if (!IsSafeFileName(asset.Name) || !IsAssetStartUrl(asset.DownloadUrl, asset.Name))
        {
            throw new UpdateDownloadException("The asset is not a valid release download.");
        }

        if (asset.Size <= 0 || asset.Size > maxBytes)
        {
            throw new UpdateDownloadException("The announced size is outside the allowed range.");
        }

        string folder;
        string path;
        try
        {
            folder = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            path = PathGuard.CombineInside(folder, asset.Name);
            Directory.CreateDirectory(folder);
            if (IsRedirected(_root) || IsRedirected(folder))
            {
                TryDeleteFolder(folder);
                throw new UpdateDownloadException("The update folder is redirected (junction or symbolic link).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            throw new UpdateDownloadException("Could not create the download folder.", ex);
        }

        FileStream? reader = null;
        try
        {
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overall.CancelAfter(_overallTimeout);
            try
            {
                await DownloadToFileAsync(asset, path, progress, overall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateDownloadException("The download timed out.");
            }

            // The handle that gets verified and held: reads allowed, writes and deletes denied.
            reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (reader.Length != asset.Size)
            {
                throw new UpdateDownloadException("The file changed after the download.");
            }

            // The launcher starts the installer by path: make sure that path still leads to this very file.
            if (!NativePaths.IsSameLocation(reader, path))
            {
                throw new UpdateDownloadException("The downloaded file is not where it should be.");
            }

            return new DownloadedUpdateFile(reader, path, folder);
        }
        catch (Exception ex)
        {
            reader?.Dispose();
            TryDeleteFolder(folder);
            if (ex is OperationCanceledException or UpdateDownloadException)
            {
                throw;
            }

            throw new UpdateDownloadException("The download failed.", ex);
        }
    }

    public void CleanUpStaleDownloads()
    {
        try
        {
            if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root) || IsRedirected(_root))
            {
                return;
            }

            var limit = DateTime.UtcNow - _staleAge;
            foreach (var dir in Directory.EnumerateDirectories(_root))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if (!IsOurFolderName(info.Name) || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    var newest = info.LastWriteTimeUtc;
                    foreach (var file in info.EnumerateFiles())
                    {
                        if (file.LastWriteTimeUtc > newest)
                        {
                            newest = file.LastWriteTimeUtc;
                        }
                    }

                    if (newest < limit)
                    {
                        TryDeleteFolder(dir);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort: whatever is locked or unreadable stays for the next start.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Never block startup.
        }
    }

    internal static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; CleanUpStaleDownloads retries on the next start.
        }
    }

    /// <summary>True when the folder, or its parent folder (<c>...\Snapdate</c>), is a junction or symbolic link.</summary>
    private static bool IsRedirected(string folder)
    {
        static bool Reparse(string? dir) =>
            !string.IsNullOrEmpty(dir) && Directory.Exists(dir) && new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint);

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return Reparse(full) || Reparse(Path.GetDirectoryName(full));
    }

    private static bool IsOurFolderName(string name) =>
        name.Length == 32 && name.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));

    private async Task DownloadToFileAsync(ReleaseAsset asset, string path, IProgress<UpdateProgress>? progress, CancellationToken overall)
    {
        using var response = await SendFollowingRedirectsAsync(asset.DownloadUrl, overall).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new UpdateDownloadException($"Unexpected HTTP status {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentEncoding.Count > 0)
        {
            throw new UpdateDownloadException("Unexpected content encoding.");
        }

        if (response.Content.Headers.ContentLength is { } declared && declared != asset.Size)
        {
            throw new UpdateDownloadException("The announced size does not match the release.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(overall).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long total = 0;
        progress?.Report(new UpdateProgress(0, asset.Size));
        while (true)
        {
            int read;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(overall))
            {
                idle.CancelAfter(_idleTimeout);
                try
                {
                    read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!overall.IsCancellationRequested)
                {
                    throw new UpdateDownloadException("The connection stalled.");
                }
            }

            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > asset.Size)
            {
                throw new UpdateDownloadException("The server sent more data than announced.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), overall).ConfigureAwait(false);
            progress?.Report(new UpdateProgress(total, asset.Size));
        }

        if (total != asset.Size)
        {
            throw new UpdateDownloadException("The download is incomplete.");
        }

        await target.FlushAsync(overall).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Uri start, CancellationToken token)
    {
        var uri = start;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowedHop(uri))
            {
                throw new UpdateDownloadException("The download address is not allowed.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(_userAgent));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new UpdateDownloadException("A redirect without a target.");
            }

            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }

        throw new UpdateDownloadException("Too many redirects.");
    }

    /// <summary>https, default port, no user info, host on the allow-list.</summary>
    internal static bool IsAllowedHop(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && AllowedHosts.Contains(uri.Host);

    /// <summary>The start URL must be exactly the asset link shape of our repository's releases.</summary>
    internal static bool IsAssetStartUrl(Uri? uri, string name)
    {
        if (uri is null || !IsAllowedHop(uri) || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.StartsWith(RepoPathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = uri.AbsolutePath[RepoPathPrefix.Length..].Split('/');
        return segments.Length == 2
            && segments[0].Length > 0
            && IsSafeFileName(segments[0])
            && segments[1] == name;
    }

    /// <summary>A plain file name: safe characters only, unchanged by the project's sanitizer, no separators.</summary>
    internal static bool IsSafeFileName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= 200
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+')
        && name is not ("." or "..")
        && !name.EndsWith('.')
        && FileNameSanitizer.SanitizeSegment(name) == name
        && Path.GetFileName(name) == name;
}
