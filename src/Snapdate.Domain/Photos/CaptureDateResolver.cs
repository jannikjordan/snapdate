using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Photos;

/// <summary>
/// Picks a photo's capture date from embedded metadata, a date in its file name and the file
/// timestamp (ADR 0007, "Date priority and conflicts"):
/// <list type="number">
/// <item>Camera metadata (a camera model was found) always wins; a file-name date on another day is
/// reported as ignored.</item>
/// <item>Otherwise a file-name date wins over app-written metadata and the file timestamp; on the same
/// day the metadata date is kept unchanged, on another day the metadata date is reported as ignored.</item>
/// <item>A date-only file name gets the file timestamp's time when that is on the same day, else 00:00:00.</item>
/// <item>No file-name date: metadata, then the file timestamp.</item>
/// </list>
/// </summary>
public static class CaptureDateResolver
{
    /// <param name="metadata">Date from embedded metadata, or null when there is none.</param>
    /// <param name="metadataFromCamera">True when the metadata reader found a camera model.</param>
    /// <param name="name">Date read from the file name, or null when no rule matched.</param>
    /// <param name="fileTimestamp">Reads the file timestamp; only called when it is needed.</param>
    public static ResolvedDate Resolve(
        CaptureDate? metadata,
        bool metadataFromCamera,
        DateRuleMatch? name,
        Func<DateTime> fileTimestamp)
    {
        ArgumentNullException.ThrowIfNull(fileTimestamp);

        if (name is null)
        {
            return new ResolvedDate(metadata ?? new CaptureDate(fileTimestamp(), DateSource.FileTimestamp));
        }

        if (metadata is { } meta)
        {
            var sameDay = DateOnly.FromDateTime(meta.Value) == name.Date.Date;
            if (sameDay)
            {
                return new ResolvedDate(meta, name.RuleName);
            }

            if (metadataFromCamera)
            {
                return new ResolvedDate(meta, name.RuleName, IgnoredFileNameDate: name.Date);
            }

            return new ResolvedDate(FromName(name.Date, fileTimestamp), name.RuleName, IgnoredMetadataDate: meta.Value);
        }

        return new ResolvedDate(FromName(name.Date, fileTimestamp), name.RuleName);
    }

    private static CaptureDate FromName(FileNameDate date, Func<DateTime> fileTimestamp)
    {
        if (date.Time is { } time)
        {
            return new CaptureDate(date.Date.ToDateTime(time), DateSource.FileName);
        }

        var stamp = fileTimestamp();
        var value = DateOnly.FromDateTime(stamp) == date.Date
            ? date.Date.ToDateTime(TimeOnly.FromDateTime(stamp))
            : date.Date.ToDateTime(TimeOnly.MinValue);
        return new CaptureDate(value, DateSource.FileName);
    }
}
