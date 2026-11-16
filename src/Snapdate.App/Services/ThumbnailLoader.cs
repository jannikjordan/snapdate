using System.Collections.Frozen;
using Avalonia.Media.Imaging;

namespace Snapdate.App.Services;

/// <summary>
/// Loads small preview images. Only common formats the built-in decoder handles are decoded, files
/// above a size limit are skipped, and decoding runs off the UI thread with a concurrency cap.
/// RAW, HEIC and video files show an icon instead.
/// </summary>
public sealed class ThumbnailLoader : IDisposable
{
    private const long MaxFileSize = 40L * 1024 * 1024;

    private static readonly FrozenSet<string> Decodable =
        new[] { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".webp", ".gif", ".bmp" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    public void Dispose() => _gate.Dispose();

    public static bool CanDecode(string path) => Decodable.Contains(Path.GetExtension(path));

    public async Task<Bitmap?> LoadAsync(string path, int width, CancellationToken cancellationToken)
    {
        if (!CanDecode(path))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () =>
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length == 0 || info.Length > MaxFileSize)
                    {
                        return null;
                    }

                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
                },
                cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A broken image must only lose its thumbnail, never crash the app.
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
