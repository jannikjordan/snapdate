using Snapdate.Domain.FileNames;

namespace Snapdate.Application.FileNames;

/// <summary>
/// What the user changed about file-name date rules: built-in rules switched off and the user's own rules.
/// Equality compares both lists by content.
/// </summary>
/// <param name="DisabledSystemRuleIds">
/// Ids of system rules the user switched off. Ids that no shipped rule has are kept, as a removed rule may come back.
/// </param>
/// <param name="CustomRules">The user's own rules in precedence order.</param>
public sealed record UserDateRules(IReadOnlyList<string> DisabledSystemRuleIds, IReadOnlyList<DateRule> CustomRules)
{
    /// <summary>Custom rules after this many are dropped.</summary>
    public const int MaxCustomRules = 100;

    /// <summary>Longest allowed rule name.</summary>
    public const int MaxNameLength = 80;

    /// <summary>Longest allowed rule id.</summary>
    public const int MaxIdLength = 64;

    /// <summary>Longest allowed syntax name.</summary>
    public const int MaxSyntaxLength = 32;

    /// <summary>Nothing disabled, no own rules.</summary>
    public static UserDateRules Empty { get; } = new([], []);

    /// <summary>True when <paramref name="id"/> matches <c>^[a-z0-9-]{1,64}$</c>.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxIdLength
        && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>
    /// True when <paramref name="rule"/> is a well-formed stored rule (custom or built-in): valid id, a name of 1–80 characters that
    /// is not only white space, 1–<see cref="DateRule.MaxPatterns"/> patterns (none null) and a syntax name.
    /// The patterns themselves are not parsed; invalid ones simply never match.
    /// </summary>
    public static bool IsValidRule(DateRule? rule) =>
        rule is not null
        && IsValidId(rule.Id)
        && IsValidName(rule.Name)
        && rule.Patterns is { Count: > 0 and <= DateRule.MaxPatterns } patterns
        && patterns.All(p => p is not null)
        && !string.IsNullOrWhiteSpace(rule.Syntax)
        && rule.Syntax.Length <= MaxSyntaxLength;

    /// <summary>True for a name of 1–<see cref="MaxNameLength"/> characters that is not only white space.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength;

    public bool Equals(UserDateRules? other) =>
        other is not null
        && (DisabledSystemRuleIds ?? []).SequenceEqual(other.DisabledSystemRuleIds ?? [], StringComparer.Ordinal)
        && (CustomRules ?? []).SequenceEqual(other.CustomRules ?? []);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var id in DisabledSystemRuleIds ?? [])
        {
            hash.Add(id, StringComparer.Ordinal);
        }

        foreach (var rule in CustomRules ?? [])
        {
            hash.Add(rule);
        }

        return hash.ToHashCode();
    }
}
