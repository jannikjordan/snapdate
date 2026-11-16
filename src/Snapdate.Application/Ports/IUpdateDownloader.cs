using Snapdate.Application.Updates;

namespace Snapdate.Application.Ports;

/// <summary>Downloads release files into a fresh private folder, enforcing network and size policy.</summary>
public interface IUpdateDownloader
{
    /// <summary>
    /// Downloads <paramref name="asset"/>. The result is exactly <c>asset.Size</c> bytes, which must not exceed
    /// <paramref name="maxBytes"/>. On any failure nothing stays on disk.
    /// </summary>
    /// <exception cref="UpdateDownloadException">Any failure other than cancellation.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested through <paramref name="cancellationToken"/>.</exception>
    Task<IDownloadedFile> DownloadAsync(
        ReleaseAsset asset,
        long maxBytes,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Removes update folders left by earlier runs. Best effort; never throws. Call once at startup.</summary>
    void CleanUpStaleDownloads();
}
