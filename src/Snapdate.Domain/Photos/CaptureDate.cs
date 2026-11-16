namespace Snapdate.Domain.Photos;

/// <summary>The moment a photo was taken (local wall-clock time) and where we learned it.</summary>
public readonly record struct CaptureDate(DateTime Value, DateSource Source)
{
    /// <summary>True when the date came from the photo itself rather than the file system.</summary>
    public bool IsFromMetadata => Source != DateSource.FileTimestamp;
}
