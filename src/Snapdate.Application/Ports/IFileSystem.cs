namespace Snapdate.Application.Ports;

/// <summary>File system access needed by the use cases.</summary>
public interface IFileSystem
{
    /// <summary>
    /// Lists files below <paramref name="root"/>. Must skip hidden/system files, symbolic links and
    /// junctions, and folders that cannot be read.
    /// </summary>
    IEnumerable<string> EnumerateFiles(string root, bool recursive);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Earliest of creation and last-write time, as local time.</summary>
    DateTime GetEarliestTimestamp(string path);

    long GetLength(string path);

    /// <summary>
    /// Copies without ever overwriting: writes to a temporary file next to the target, verifies the
    /// size and atomically renames it. Throws <see cref="TargetExistsException"/> if the target appeared.
    /// </summary>
    Task CopyAsync(string source, string target, CancellationToken cancellationToken);

    /// <summary>
    /// Moves without ever overwriting. Same volume: atomic rename. Otherwise: verified copy, then delete.
    /// Throws <see cref="TargetExistsException"/> if the target appeared.
    /// </summary>
    Task MoveAsync(string source, string target, CancellationToken cancellationToken);

    void CreateDirectory(string path);

    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);
}
