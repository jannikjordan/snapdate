using Snapdate.Domain.Photos;

namespace Snapdate.Application.Scanning;

/// <summary>A file found in the source folder, with its capture date already resolved.</summary>
/// <param name="SourcePath">Full path of the file.</param>
/// <param name="OriginalName">File name without extension.</param>
/// <param name="Extension">Extension including the dot.</param>
/// <param name="ResolvedDate">
/// The capture date and how it was chosen: matching date rule, ignored metadata or file-name date and
/// the "Dates differ" flag (see <see cref="CaptureDateResolver"/>).
/// </param>
/// <param name="CameraModel">Camera model from metadata, if any.</param>
/// <param name="SizeBytes">File size.</param>
public sealed record ScannedPhoto(
    string SourcePath,
    string OriginalName,
    string Extension,
    ResolvedDate ResolvedDate,
    string? CameraModel,
    long SizeBytes)
{
    /// <summary>A photo whose date was not influenced by a file-name rule.</summary>
    public ScannedPhoto(string SourcePath, string OriginalName, string Extension, CaptureDate Date, string? CameraModel, long SizeBytes)
        : this(SourcePath, OriginalName, Extension, new ResolvedDate(Date), CameraModel, SizeBytes)
    {
    }

    /// <summary>The capture date to use; shorthand for <see cref="ResolvedDate"/>.<see cref="ResolvedDate.Date"/>.</summary>
    public CaptureDate Date => ResolvedDate.Date;
}
