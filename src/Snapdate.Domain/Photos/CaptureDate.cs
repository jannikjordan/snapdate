namespace Snapdate.Domain.Photos;

/// <summary>The moment a photo was taken (local wall-clock time) and where we learned it.</summary>
public readonly record struct CaptureDate(DateTime Value, DateSource Source)
{
    /// <summary>True when the date came from the photo's embedded metadata (not the file name or file system).</summary>
    public bool IsFromMetadata => Source is not (DateSource.FileTimestamp or DateSource.FileName);

    /// <summary>True when the date was read from the file name by a date rule.</summary>
    public bool IsFromFileName => Source == DateSource.FileName;
}
