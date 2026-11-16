namespace Snapdate.Domain.Planning;

/// <summary>
/// Finds a free target name: <c>name.jpg</c>, then <c>name (2).jpg</c>, <c>name (3).jpg</c>, …
/// Existing files are never overwritten. Comparison is case-insensitive like NTFS.
/// </summary>
public sealed class UniqueNameResolver(Func<string, bool> existsOnDisk)
{
    /// <summary>Upper bound on suffix attempts, to make runaway loops impossible.</summary>
    public const int MaxAttempts = 10_000;

    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reserves and returns a unique relative path.</summary>
    /// <param name="relativeStem">Relative path without extension, <c>/</c>-separated.</param>
    /// <param name="extension">Extension including the dot.</param>
    /// <param name="wasRenamed">True when a " (n)" suffix had to be added.</param>
    public string Reserve(string relativeStem, string extension, out bool wasRenamed)
    {
        for (var n = 1; n <= MaxAttempts; n++)
        {
            var candidate = n == 1 ? relativeStem + extension : $"{relativeStem} ({n}){extension}";
            if (!_reserved.Contains(candidate) && !existsOnDisk(candidate))
            {
                _reserved.Add(candidate);
                wasRenamed = n > 1;
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free name found for '{relativeStem}{extension}'.");
    }
}
