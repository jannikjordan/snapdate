namespace Snapdate.Domain.FileNames;

/// <summary>
/// A named list of 1–<see cref="MaxPatterns"/> patterns that read a date from a file name.
/// Equality compares the patterns by content.
/// </summary>
/// <param name="Id">Permanent id, e.g. <c>whatsapp-android</c> or <c>custom-0a1b2c3d</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Origin">Shipped with the app or created by the user.</param>
/// <param name="Enabled">Disabled rules never match.</param>
/// <param name="Patterns">Patterns in <paramref name="Syntax"/>, tried in order.</param>
/// <param name="Syntax">How the patterns are written; see <see cref="DateRuleSyntax"/>.</param>
public sealed record DateRule(
    string Id,
    string Name,
    RuleOrigin Origin,
    bool Enabled,
    IReadOnlyList<string> Patterns,
    string Syntax = DateRuleSyntax.Pattern)
{
    /// <summary>Patterns after this many are ignored.</summary>
    public const int MaxPatterns = 10;

    /// <summary>False when the rule was written by a newer version in a syntax this one cannot match.</summary>
    public bool IsSyntaxSupported => DateRuleSyntax.IsSupported(Syntax);

    public bool Equals(DateRule? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && Origin == other.Origin
        && Enabled == other.Enabled
        && string.Equals(Syntax, other.Syntax, StringComparison.Ordinal)
        && (Patterns ?? []).SequenceEqual(other.Patterns ?? [], StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Origin);
        hash.Add(Enabled);
        hash.Add(Syntax, StringComparer.Ordinal);
        foreach (var pattern in Patterns ?? [])
        {
            hash.Add(pattern, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
