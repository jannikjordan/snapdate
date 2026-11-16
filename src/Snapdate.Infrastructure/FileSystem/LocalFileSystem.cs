using Snapdate.Application.Ports;

namespace Snapdate.Infrastructure.FileSystem;

/// <summary>
/// Local disk access with "never overwrite, never lose a photo" semantics:
/// copies go to a temporary file first and are renamed into place only after the size was verified.
/// </summary>
public sealed class LocalFileSystem : IFileSystem
{
    private const int BufferSize = 1024 * 1024;
    private const string TempSuffix = ".snapdate-tmp";

    public IEnumerable<string> EnumerateFiles(string root, bool recursive) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            // Never follow symbolic links/junctions, skip hidden and system files.
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
            MatchType = MatchType.Simple,
        });

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public DateTime GetEarliestTimestamp(string path)
    {
        var info = new FileInfo(path);
        var created = info.CreationTime;
        var modified = info.LastWriteTime;
        return created < modified ? created : modified;
    }

    public long GetLength(string path) => new FileInfo(path).Length;

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (File.Exists(target))
        {
            throw new TargetExistsException($"'{target}' already exists.");
        }

        var temp = target + "." + Guid.NewGuid().ToString("N")[..8] + TempSuffix;
        try
        {
            var sourceInfo = new FileInfo(source);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            var copied = new FileInfo(temp);
            if (copied.Length != sourceInfo.Length)
            {
                throw new IOException($"Copy of '{source}' is incomplete ({copied.Length} of {sourceInfo.Length} bytes).");
            }

            // Keep the original timestamps so other photo tools still sort correctly.
            File.SetCreationTime(temp, sourceInfo.CreationTime);
            File.SetLastWriteTime(temp, sourceInfo.LastWriteTime);

            try
            {
                File.Move(temp, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
                throw new TargetExistsException($"'{target}' already exists.");
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public async Task MoveAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (File.Exists(target))
        {
            throw new TargetExistsException($"'{target}' already exists.");
        }

        if (SameVolume(source, target))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Atomic rename on the same drive: no data is copied, nothing can be half-written.
                File.Move(source, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
                throw new TargetExistsException($"'{target}' already exists.");
            }

            return;
        }

        await CopyAsync(source, target, cancellationToken).ConfigureAwait(false);

        // Only delete the original once the verified copy is in place.
        if (new FileInfo(target).Length == new FileInfo(source).Length)
        {
            File.Delete(source);
        }
    }

    public async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(a)),
            Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
