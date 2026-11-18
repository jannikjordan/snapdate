namespace Snapdate.Domain.FileNames;

/// <summary>
/// How the patterns of a <see cref="DateRule"/> are written. Stored as text so rules written by a newer
/// version (with a syntax this version does not know) survive a round trip; such rules never match.
/// </summary>
public static class DateRuleSyntax
{
    /// <summary>Token patterns such as <c>IMG-{YYYY}{MM}{DD}-WA*</c> (see <see cref="DatePattern"/>).</summary>
    public const string Pattern = "pattern";

    /// <summary>True when this version can match rules written in <paramref name="syntax"/>.</summary>
    public static bool IsSupported(string? syntax) => string.Equals(syntax, Pattern, StringComparison.Ordinal);
}
