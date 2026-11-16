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

    public int FromMetadataCount => Items.Count(i => i.Photo.Date.IsFromMetadata);

    public int FromFileDateCount => Items.Count(i => !i.Photo.Date.IsFromMetadata);

    public int RenamedCount => Items.Count(i => i.WasRenamed);
}
