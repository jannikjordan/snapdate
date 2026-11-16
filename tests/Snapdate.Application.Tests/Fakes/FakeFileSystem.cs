using Snapdate.Application.Ports;

namespace Snapdate.Application.Tests.Fakes;

/// <summary>In-memory file system: paths are full paths, compared case-insensitively.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    public Dictionary<string, (long Length, DateTime Timestamp)> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> TextFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths for which copy/move throws an IOException (simulated disk error).</summary>
    public HashSet<string> FailingSources { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Called right before a copy/move; lets tests create files "behind our back".</summary>
    public Action<string>? BeforeTransfer { get; set; }

    public List<(string Op, string Source, string Target)> Operations { get; } = [];

    public void AddFile(string path, long length = 100, DateTime? timestamp = null)
    {
        Files[Path.GetFullPath(path)] = (length, timestamp ?? new DateTime(2020, 1, 1));
        Directories.Add(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public IEnumerable<string> EnumerateFiles(string root, bool recursive)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return Files.Keys.Where(f =>
        {
            var dir = Path.GetDirectoryName(f)!;
            return recursive
                ? dir.Equals(full, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                : dir.Equals(full, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    public bool FileExists(string path) => Files.ContainsKey(Path.GetFullPath(path));

    public bool DirectoryExists(string path) => Directories.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));

    public DateTime GetEarliestTimestamp(string path) => Files[Path.GetFullPath(path)].Timestamp;

    public long GetLength(string path) => Files[Path.GetFullPath(path)].Length;

    public Task CopyAsync(string source, string target, CancellationToken cancellationToken) =>
        Transfer("copy", source, target, cancellationToken);

    public Task MoveAsync(string source, string target, CancellationToken cancellationToken) =>
        Transfer("move", source, target, cancellationToken);

    public void CreateDirectory(string path) => Directories.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        TextFiles[path] = contents;
        return Task.CompletedTask;
    }

    private Task Transfer(string op, string source, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeTransfer?.Invoke(target);

        if (FailingSources.Contains(source))
        {
            throw new IOException("Simulated disk error.");
        }

        if (FileExists(target))
        {
            throw new TargetExistsException(target);
        }

        Files[Path.GetFullPath(target)] = Files[Path.GetFullPath(source)];
        if (op == "move")
        {
            Files.Remove(Path.GetFullPath(source));
        }

        Operations.Add((op, source, target));
        return Task.CompletedTask;
    }
}
