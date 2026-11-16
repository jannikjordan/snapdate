using Snapdate.Application.Ports;
using Snapdate.Domain.Paths;

namespace Snapdate.Application.Planning;

/// <summary>Problems with the chosen folders, in the order the user should fix them.</summary>
public enum FolderProblem
{
    None,
    SourceMissing,
    TargetMissing,
    SourceNotFound,
    TargetInsideSource,
    SourceInsideTarget,
}

/// <summary>Validates the source/target folder combination before scanning.</summary>
public static class FolderCheck
{
    public static FolderProblem Check(IFileSystem fileSystem, string? source, string? target, bool recursive)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (string.IsNullOrWhiteSpace(source))
        {
            return FolderProblem.SourceMissing;
        }

        if (!fileSystem.DirectoryExists(source))
        {
            return FolderProblem.SourceNotFound;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return FolderProblem.TargetMissing;
        }

        var same = PathGuard.IsInside(source, target) && PathGuard.IsInside(target, source);
        if (same)
        {
            // Renaming in place is allowed: everything is read before anything is written.
            return FolderProblem.None;
        }

        if (recursive && PathGuard.IsInside(source, target))
        {
            // A later run would pick up its own output.
            return FolderProblem.TargetInsideSource;
        }

        if (PathGuard.IsInside(target, source))
        {
            return FolderProblem.SourceInsideTarget;
        }

        return FolderProblem.None;
    }
}
