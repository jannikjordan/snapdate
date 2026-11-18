namespace Snapdate.Domain.Photos;

/// <summary>Where a photo's capture date came from, best first.</summary>
public enum DateSource
{
    /// <summary>EXIF DateTimeOriginal — the moment the shutter was pressed.</summary>
    ExifOriginal,

    /// <summary>EXIF DateTimeDigitized.</summary>
    ExifDigitized,

    /// <summary>EXIF DateTime (last modified by the camera/software).</summary>
    ExifDateTime,

    /// <summary>XMP photoshop:DateCreated / xmp:CreateDate.</summary>
    Xmp,

    /// <summary>QuickTime/MP4 creation time (videos, some HEIC files).</summary>
    QuickTime,

    /// <summary>PNG tEXt/iTXt "Creation Time" chunk.</summary>
    PngText,

    /// <summary>A date read from the file name by a date rule (e.g. WhatsApp's <c>IMG-YYYYMMDD-WA…</c>).</summary>
    FileName,

    /// <summary>No metadata: the earliest of the file's created/modified timestamps.</summary>
    FileTimestamp,
}
