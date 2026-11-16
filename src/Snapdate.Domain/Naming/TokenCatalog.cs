using System.Collections.Frozen;

namespace Snapdate.Domain.Naming;

/// <summary>Maps placeholder text (case-sensitive, without braces) to <see cref="TokenKind"/>.</summary>
public static class TokenCatalog
{
    private static readonly FrozenDictionary<string, TokenKind> ByName = new Dictionary<string, TokenKind>(StringComparer.Ordinal)
    {
        ["YYYY"] = TokenKind.Year4,
        ["YY"] = TokenKind.Year2,
        ["MM"] = TokenKind.Month2,
        ["M"] = TokenKind.Month1,
        ["MMM"] = TokenKind.MonthNameShort,
        ["MMMM"] = TokenKind.MonthNameLong,
        ["DD"] = TokenKind.Day2,
        ["D"] = TokenKind.Day1,
        ["ddd"] = TokenKind.WeekdayShort,
        ["dddd"] = TokenKind.WeekdayLong,
        ["HH"] = TokenKind.Hour2,
        ["H"] = TokenKind.Hour1,
        ["mm"] = TokenKind.Minute2,
        ["m"] = TokenKind.Minute1,
        ["ss"] = TokenKind.Second2,
        ["s"] = TokenKind.Second1,
        ["name"] = TokenKind.OriginalName,
        ["camera"] = TokenKind.Camera,
        ["counter"] = TokenKind.Counter,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<TokenKind, string> ByKind =
        ByName.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>All supported placeholder names, e.g. <c>YYYY</c>.</summary>
    public static IEnumerable<string> Names => ByName.Keys;

    public static bool TryGet(string name, out TokenKind kind) => ByName.TryGetValue(name, out kind);

    /// <summary>The placeholder text without braces, e.g. <c>MM</c>.</summary>
    public static string NameOf(TokenKind kind) => ByKind[kind];

    /// <summary>
    /// The same token with/without leading zero (e.g. <c>MM</c> ⇄ <c>M</c>), or null when the
    /// token has no such variant.
    /// </summary>
    public static TokenKind? LeadingZeroCounterpart(TokenKind kind) => kind switch
    {
        TokenKind.Month2 => TokenKind.Month1,
        TokenKind.Month1 => TokenKind.Month2,
        TokenKind.Day2 => TokenKind.Day1,
        TokenKind.Day1 => TokenKind.Day2,
        TokenKind.Hour2 => TokenKind.Hour1,
        TokenKind.Hour1 => TokenKind.Hour2,
        TokenKind.Minute2 => TokenKind.Minute1,
        TokenKind.Minute1 => TokenKind.Minute2,
        TokenKind.Second2 => TokenKind.Second1,
        TokenKind.Second1 => TokenKind.Second2,
        _ => null,
    };

    /// <summary>
    /// The alternative form of a token the user can switch to by clicking it: leading zero on/off,
    /// 4/2-digit year, short/long month and weekday names. Null when there is none.
    /// </summary>
    public static TokenKind? Alternate(TokenKind kind) => LeadingZeroCounterpart(kind) ?? kind switch
    {
        TokenKind.Year4 => TokenKind.Year2,
        TokenKind.Year2 => TokenKind.Year4,
        TokenKind.MonthNameShort => TokenKind.MonthNameLong,
        TokenKind.MonthNameLong => TokenKind.MonthNameShort,
        TokenKind.WeekdayShort => TokenKind.WeekdayLong,
        TokenKind.WeekdayLong => TokenKind.WeekdayShort,
        _ => null,
    };

    /// <summary>True for tokens that print a leading zero.</summary>
    public static bool HasLeadingZero(TokenKind kind) =>
        kind is TokenKind.Month2 or TokenKind.Day2 or TokenKind.Hour2 or TokenKind.Minute2 or TokenKind.Second2;
}
