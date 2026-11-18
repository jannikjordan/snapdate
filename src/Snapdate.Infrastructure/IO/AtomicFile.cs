namespace Snapdate.Infrastructure.IO;

/// <summary>Writes small files so that readers see either the old or the new content, never a half-written file.</summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="content"/> to a temp file next to <paramref name="path"/> and moves it over the target.
    /// Creates the folder if needed. Returns false (and removes the temp file) when the write fails.
    /// </summary>
    public static bool TryWrite(string path, byte[] content)
    {
        // A unique temp name per write: even another process saving at the same time cannot share it.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            return false;
        }
    }
}
