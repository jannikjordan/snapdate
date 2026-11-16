using System.Text;
using Snapdate.Application.Installation;

namespace Snapdate.Infrastructure.Installation;

/// <summary>Strict parser for <c>snapdate-install.ini</c>, the marker file the installer writes next to the exe.</summary>
/// <remarks>
/// Format (written by <c>installer/Snapdate.iss</c>):
/// <code>
/// [Install]
/// Scope=user
/// </code>
/// <c>Scope</c> is exactly <c>user</c> or <c>all</c>. Blank lines and lines starting with <c>;</c> are
/// ignored and keys other than <c>Scope</c> are ignored (room for later versions). A second or differently
/// named section, a duplicate or missing <c>Scope</c>, an unknown value, invalid UTF-8 or any line that is
/// not <c>key=value</c> makes the whole file invalid.
/// </remarks>
internal static class InstallMarker
{
    public const string FileName = "snapdate-install.ini";

    /// <summary>Real markers are two lines; anything bigger is not ours.</summary>
    public const int MaxBytes = 1024;

    /// <returns>The scope, or <c>null</c> when the content is not a valid marker.</returns>
    public static InstallScope? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaxBytes)
        {
            return null;
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var sawSection = false;
        InstallScope? scope = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (sawSection || line != "[Install]")
                {
                    return null;
                }

                sawSection = true;
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!sawSection || equals <= 0)
            {
                return null;
            }

            if (line[..equals].TrimEnd() != "Scope")
            {
                continue;
            }

            if (scope is not null)
            {
                return null;
            }

            scope = line[(equals + 1)..].TrimStart() switch
            {
                "user" => InstallScope.CurrentUser,
                "all" => InstallScope.AllUsers,
                _ => null,
            };
            if (scope is null)
            {
                return null;
            }
        }

        return sawSection ? scope : null;
    }
}
