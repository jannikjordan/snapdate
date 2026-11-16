using System.Globalization;
using Snapdate.Application.Ports;
using Snapdate.Application.Scanning;
using Snapdate.Domain.Naming;
using Snapdate.Domain.Paths;
using Snapdate.Domain.Photos;
using Snapdate.Domain.Planning;

namespace Snapdate.Application.Planning;

/// <summary>
/// Turns scanned photos into a <see cref="RenamePlan"/>: orders them by capture date, applies the
/// pattern, keeps the original extension (lower-case) and resolves duplicates against both the plan
/// and files already in the target folder.
/// </summary>
public sealed class BuildPlanUseCase(IFileSystem fileSystem)
{
    /// <param name="photos">Scanned files.</param>
    /// <param name="pattern">Name pattern.</param>
    /// <param name="culture">Culture for month and weekday names.</param>
    /// <param name="sourceRoot">Source folder.</param>
    /// <param name="targetRoot">
    /// Target folder. May be null while the user has not chosen one yet: names are then only made
    /// unique within the plan, and the plan cannot be executed.
    /// </param>
    /// <param name="mode">Copy or move.</param>
    public RenamePlan Build(
        IEnumerable<ScannedPhoto> photos,
        NamePattern pattern,
        CultureInfo culture,
        string sourceRoot,
        string? targetRoot,
        TransferMode mode)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(pattern);

        var ordered = photos
            .OrderBy(p => p.Date.Value)
            .ThenBy(p => p.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolver = new UniqueNameResolver(relative =>
            targetRoot is not null && fileSystem.FileExists(PathGuard.CombineInside(targetRoot, relative)));

        var items = new List<RenameItem>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var photo = ordered[i];
            var facts = new PhotoFacts(photo.OriginalName, photo.Extension, photo.Date, photo.CameraModel, i + 1);
            var stem = pattern.Render(facts, culture);
            var extension = photo.Extension.ToLowerInvariant();

            // Renaming in place to the name it already has: keep it, the executor skips it.
            var target = targetRoot is not null && IsSameFile(sourceRoot, targetRoot, photo.SourcePath, stem + extension)
                ? stem + extension
                : resolver.Reserve(stem, extension, out _);

            var wasRenamed = !string.Equals(target, stem + extension, StringComparison.OrdinalIgnoreCase);
            items.Add(new RenameItem(photo, target, wasRenamed));
        }

        return new RenamePlan(sourceRoot, targetRoot ?? string.Empty, mode, items);
    }

    private static bool IsSameFile(string sourceRoot, string targetRoot, string sourcePath, string relativeTarget)
    {
        if (!PathGuard.IsInside(sourceRoot, targetRoot) || !PathGuard.IsInside(targetRoot, sourceRoot))
        {
            return false;
        }

        var target = PathGuard.CombineInside(targetRoot, relativeTarget);
        return string.Equals(Path.GetFullPath(sourcePath), target, StringComparison.OrdinalIgnoreCase);
    }
}
