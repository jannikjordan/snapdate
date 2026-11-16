namespace Snapdate.Domain.Paths;

/// <summary>Defence in depth against path traversal: a target must stay inside its root.</summary>
public static class PathGuard
{
    /// <summary>Combines <paramref name="root"/> with a relative path and verifies the result stays inside it.</summary>
    /// <exception cref="InvalidOperationException">The combined path escapes the root.</exception>
    public static string CombineInside(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Target path must be relative: '{relativePath}'.");
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath));

        if (!IsInside(fullRoot, full) || PathsEqual(fullRoot, full))
        {
            throw new InvalidOperationException($"Target path escapes the destination folder: '{relativePath}'.");
        }

        return full;
    }

    /// <summary>True when <paramref name="candidate"/> is <paramref name="root"/> itself or below it.</summary>
    public static bool IsInside(string root, string candidate)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));

        return PathsEqual(full, fullRoot)
            || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);
}
