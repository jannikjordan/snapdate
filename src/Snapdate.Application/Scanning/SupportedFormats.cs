using System.Collections.Frozen;

namespace Snapdate.Application.Scanning;

/// <summary>File types Snapdate picks up from the source folder.</summary>
public static class SupportedFormats
{
    public static readonly FrozenSet<string> Photos = new[]
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".heic", ".heif", ".avif", ".webp",
        ".tif", ".tiff", ".gif", ".bmp",
        // Camera RAW
        ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".orf",
        ".rw2", ".raf", ".pef", ".srw", ".x3f", ".3fr", ".erf", ".kdc", ".mrw",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static readonly FrozenSet<string> Videos = new[]
    {
        ".mp4", ".mov", ".m4v", ".3gp",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return Photos.Contains(extension) || Videos.Contains(extension);
    }
}
