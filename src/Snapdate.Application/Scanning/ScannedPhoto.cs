using Snapdate.Domain.FileNames;
using Snapdate.Domain.Photos;

namespace Snapdate.Application.Scanning;

/// <summary>A file found in the source folder, with its capture date already resolved.</summary>
/// <param name="SourcePath">Full path of the file.</param>
/// <param name="OriginalName">File name without extension.</param>
/// <param name="Extension">Extension including the dot.</param>
/// <param name="Date">The resolved capture date (see <see cref="CaptureDateResolver"/>).</param>
/// <param name="CameraModel">Camera model from metadata, if any.</param>
/// <param name="SizeBytes">File size.</param>
/// <param name="DateRuleName">
/// Display name of the date rule that matched the file name, or null. Set even when the name's date
/// was not used.
/// </param>
/// <param name="IgnoredMetadataDate">App-written metadata date ignored in favour of the file name.</param>
/// <param name="IgnoredFileNameDate">File-name date ignored in favour of camera metadata.</param>
public sealed record ScannedPhoto(
    string SourcePath,
    string OriginalName,
    string Extension,
    CaptureDate Date,
    string? CameraModel,
    long SizeBytes,
    string? DateRuleName = null,
    DateTime? IgnoredMetadataDate = null,
    FileNameDate? IgnoredFileNameDate = null)
{
    /// <summary>True when metadata and file name give different days ("Dates differ").</summary>
    public bool DatesDiffer => IgnoredMetadataDate is not null || IgnoredFileNameDate is not null;
}
