using System.Collections.Frozen;
using System.Text;

namespace Snapdate.Domain.Paths;

/// <summary>
/// Turns arbitrary text (including untrusted photo metadata) into safe Windows path segments.
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>Longest segment we produce; leaves room for " (99)" and an extension.</summary>
    public const int MaxSegmentLength = 180;

    /// <summary>Characters Windows forbids in file and folder names (besides control characters).</summary>
    public static readonly FrozenSet<char> InvalidChars =
        new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }.ToFrozenSet();

    private static readonly FrozenSet<string> ReservedNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
        "CONIN$", "CONOUT$",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsInvalidChar(char c) => c < 32 || c == 127 || InvalidChars.Contains(c);

    /// <summary>
    /// Cleans a placeholder value (e.g. a camera model) so it can never add folders or break a name:
    /// path separators and invalid characters become <c>-</c>, whitespace runs collapse.
    /// </summary>
    public static string SanitizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var c in value.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(IsInvalidChar(c) ? '-' : c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Makes one path segment safe: replaces invalid characters, removes trailing dots/spaces,
    /// neutralises "." / ".." and reserved device names, and caps the length.
    /// Never returns an empty string.
    /// </summary>
    public static string SanitizeSegment(string? segment)
    {
        var builder = new StringBuilder(segment?.Length ?? 0);
        foreach (var c in segment ?? string.Empty)
        {
            builder.Append(IsInvalidChar(c) ? '-' : c);
        }

        var result = builder.ToString().Trim().TrimEnd('.', ' ');

        if (result.Length > MaxSegmentLength)
        {
            result = result[..MaxSegmentLength].TrimEnd('.', ' ');
        }

        if (result.Length == 0)
        {
            return "_";
        }

        var stem = result.Split('.', 2)[0].TrimEnd();
        if (ReservedNames.Contains(stem))
        {
            result = "_" + result;
        }

        return result;
    }
}
