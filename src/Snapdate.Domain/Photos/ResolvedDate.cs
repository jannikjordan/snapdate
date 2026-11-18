using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Photos;

/// <summary>The result of <see cref="CaptureDateResolver.Resolve"/>.</summary>
/// <param name="Date">The capture date to use.</param>
/// <param name="RuleName">
/// Display name of the date rule that matched the file name, or null when no rule matched. Set even
/// when the name's date was not used (camera metadata won, or the metadata was on the same day).
/// </param>
/// <param name="IgnoredMetadataDate">
/// The app-written metadata date that was ignored because the file name gives another day; null otherwise.
/// </param>
/// <param name="IgnoredFileNameDate">
/// The file-name date that was ignored because camera metadata gives another day; null otherwise.
/// </param>
public sealed record ResolvedDate(
    CaptureDate Date,
    string? RuleName = null,
    DateTime? IgnoredMetadataDate = null,
    FileNameDate? IgnoredFileNameDate = null)
{
    /// <summary>
    /// True when metadata and file name name different days ("Dates differ"); the date that was not
    /// used is <see cref="IgnoredMetadataDate"/> or <see cref="IgnoredFileNameDate"/>.
    /// </summary>
    public bool DatesDiffer => IgnoredMetadataDate is not null || IgnoredFileNameDate is not null;
}
