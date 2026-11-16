using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Snapdate.Infrastructure.Updates;

/// <summary>
/// Asks Windows where an open file really lives. Used to catch a junction or symlink that redirects the update
/// folder somewhere else (source-generated P/Invoke, trim and AOT safe).
/// </summary>
internal static partial class NativePaths
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] buffer, uint bufferLength, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetLongPathName(string shortPath, [Out] char[] buffer, uint bufferLength);

    /// <summary>
    /// True when the open <paramref name="stream"/> is the file at <paramref name="path"/> without any junction,
    /// symlink or other redirection in between. Always true on non-Windows systems (never shipped there).
    /// </summary>
    internal static bool IsSameLocation(FileStream stream, string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var actual = GetFinalPath(stream.SafeFileHandle);
        var expected = GetLongPath(Path.GetFullPath(path));
        return actual is not null
            && expected is not null
            && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The final, normalized path of an open handle without the <c>\\?\</c> prefix; null on failure.</summary>
    internal static string? GetFinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                return StripPrefix(new string(buffer, 0, (int)length));
            }

            buffer = new char[length + 1];
        }

        return null;
    }

    /// <summary>Expands 8.3 short names so that both sides of the comparison use long names.</summary>
    internal static string? GetLongPath(string path)
    {
        var buffer = new char[512];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var length = GetLongPathName(path, buffer, (uint)buffer.Length);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                return StripPrefix(new string(buffer, 0, (int)length));
            }

            buffer = new char[length + 1];
        }

        return null;
    }

    internal static string StripPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }
}
