using Snapdate.Domain.Photos;
using Snapdate.Domain.Planning;

namespace Snapdate.Application.Planning;

/// <summary>The full preview: every file with its new name. Nothing has been written yet.</summary>
public sealed record RenamePlan(
    string SourceRoot,
    string TargetRoot,
    TransferMode Mode,
    IReadOnlyList<RenameItem> Items)
{
    /// <summary>False for preview-only plans built before a target folder was chosen.</summary>
    public bool CanExecute => !string.IsNullOrWhiteSpace(TargetRoot) && Items.Count > 0;

    /// <summary>Items dated from embedded metadata.</summary>
    public int FromMetadataCount => Items.Count(i => i.Photo.Date.IsFromMetadata);

    /// <summary>Items dated from the file name by a date rule.</summary>
    public int FromFileNameCount => Items.Count(i => i.Photo.Date.IsFromFileName);

    /// <summary>Items dated from the file timestamp (no metadata, no file-name date).</summary>
    public int FromFileDateCount => Items.Count(i => i.Photo.Date.Source == DateSource.FileTimestamp);

    public int RenamedCount => Items.Count(i => i.WasRenamed);
}
