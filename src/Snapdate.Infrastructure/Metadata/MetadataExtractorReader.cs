using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using Snapdate.Application.Ports;
using Snapdate.Domain.Photos;
using MeDirectory = MetadataExtractor.Directory;

namespace Snapdate.Infrastructure.Metadata;

/// <summary>
/// Reads capture dates and camera model with MetadataExtractor. Files are untrusted input: they are
/// opened read-only, only metadata segments are parsed (no pixel decoding) and every parser failure
/// is swallowed so the caller falls back to the file timestamp.
/// </summary>
public sealed class MetadataExtractorReader(TimeProvider clock) : IMetadataReader
{
    private static readonly string[] XmpDateKeys =
    [
        "exif:DateTimeOriginal",
        "photoshop:DateCreated",
        "xmp:CreateDate",
    ];

    public PhotoMetadata Read(string path)
    {
        IReadOnlyList<MeDirectory> directories;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            directories = ImageMetadataReader.ReadMetadata(stream);
        }
#pragma warning disable CA1031 // Parser bugs on hostile input must never crash the app.
        catch (Exception)
#pragma warning restore CA1031
        {
            return PhotoMetadata.None;
        }

        var camera = CameraModel(directories);
        var (taken, source) = CaptureDate(directories);
        return new PhotoMetadata(taken, source, camera);
    }

    private (DateTime? Taken, DateSource? Source) CaptureDate(IReadOnlyList<MeDirectory> directories)
    {
        foreach (var sub in directories.OfType<ExifSubIfdDirectory>())
        {
            if (TryExifDate(sub, ExifDirectoryBase.TagDateTimeOriginal, out var original))
            {
                return (original, DateSource.ExifOriginal);
            }

            if (TryExifDate(sub, ExifDirectoryBase.TagDateTimeDigitized, out var digitized))
            {
                return (digitized, DateSource.ExifDigitized);
            }
        }

        foreach (var ifd0 in directories.OfType<ExifIfd0Directory>())
        {
            if (TryExifDate(ifd0, ExifDirectoryBase.TagDateTime, out var dateTime))
            {
                return (dateTime, DateSource.ExifDateTime);
            }
        }

        foreach (var xmp in directories.OfType<XmpDirectory>())
        {
            var properties = xmp.GetXmpProperties();
            foreach (var key in XmpDateKeys)
            {
                if (properties.TryGetValue(key, out var text) && TryParseIso(text, out var value))
                {
                    return (value, DateSource.Xmp);
                }
            }
        }

        foreach (var movie in directories.OfType<QuickTimeMovieHeaderDirectory>())
        {
            if (movie.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var created)
                && Plausible(DateTime.SpecifyKind(created, DateTimeKind.Utc).ToLocalTime()) is { } local)
            {
                return (local, DateSource.QuickTime);
            }
        }

        foreach (var png in directories.OfType<PngDirectory>())
        {
            if (png.GetObject(PngDirectory.TagTextualData) is IEnumerable<MetadataExtractor.KeyValuePair> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (string.Equals(pair.Key, "Creation Time", StringComparison.OrdinalIgnoreCase)
                        && TryParseIso(pair.Value.ToString(), out var value))
                    {
                        return (value, DateSource.PngText);
                    }
                }
            }
        }

        return (null, null);
    }

    private bool TryExifDate(MeDirectory directory, int tag, out DateTime value)
    {
        value = default;
        try
        {
            if (directory.TryGetDateTime(tag, out var raw) && Plausible(raw) is { } ok)
            {
                value = ok;
                return true;
            }
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            // malformed value: ignore and try the next source
        }

        return false;
    }

    private bool TryParseIso(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // XMP/PNG dates usually carry an offset; keep the wall-clock time the photographer saw.
        if (DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto)
            && Plausible(dto.DateTime) is { } ok)
        {
            value = ok;
            return true;
        }

        if (DateTime.TryParseExact(text.Trim(), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exifStyle)
            && Plausible(exifStyle) is { } ok2)
        {
            value = ok2;
            return true;
        }

        return false;
    }

    /// <summary>Rejects unset camera clocks and future dates (see <see cref="DatePlausibility"/>).</summary>
    private DateTime? Plausible(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return DatePlausibility.IsPlausible(unspecified, clock.GetLocalNow().DateTime) ? unspecified : null;
    }

    private static string? CameraModel(IReadOnlyList<MeDirectory> directories)
    {
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var model = ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim().TrimEnd('\0');
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }
}
