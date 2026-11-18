using System.Text.RegularExpressions;

namespace Snapdate.Domain.FileNames;

/// <summary>
/// A user-written regular expression that reads a date from a file name (without extension), for advanced users
/// (<see cref="DateRuleSyntax.Regex"/>). The date parts are named groups: <c>year</c> (or <c>yy</c> for a two-digit
/// year), <c>month</c> and <c>day</c> are required; <c>hour</c>, <c>minute</c> and <c>second</c> are optional.
/// The expression must match the whole name.
/// </summary>
/// <remarks>
/// The text is untrusted input. It is limited to <see cref="MaxLength"/> characters and must be in the subset that
/// <see cref="ExpressionGrammar"/> parses (an allowlist: literals, escapes, <c>\d \s \w .</c>, simple classes, plain,
/// non-capturing and named groups, <c>|</c>, quantifiers up to <see cref="MaxRepetition"/>), with no repeated part of
/// varying width and within a complexity budget. Only then is it compiled by the engine, which must agree: on its own,
/// and wrapped as <c>^(?:…)$</c> with the same options and timeout as token patterns. <c>NonBacktracking</c> takes time
/// linear in the name with a factor that depends on the expression — the grammar's limits keep that factor small; the
/// engine checks the timeout only now and then, so it is a last resort. Every problem becomes a
/// <see cref="DatePatternError"/>; nothing throws. The captured values pass the same ASCII-digit, calendar and
/// plausibility checks as token patterns.
/// </remarks>
public sealed class DateExpression : IFileNameDateMatcher
{
    /// <summary>Maximum expression length in characters.</summary>
    public const int MaxLength = 300;

    /// <summary>Largest count allowed in <c>{n}</c>, <c>{n,}</c> and <c>{n,m}</c>; dates need at most 4.</summary>
    public const int MaxRepetition = 20;

    /// <summary>The group names an expression may use.</summary>
    public static IReadOnlyList<string> GroupNames { get; } =
    [
        DateCapture.Year,
        DateCapture.TwoDigitYear,
        DateCapture.Month,
        DateCapture.Day,
        DateCapture.Hour,
        DateCapture.Minute,
        DateCapture.Second,
    ];

    private readonly Regex _regex;

    /// <summary>Wraps an already compiled expression; for <see cref="Parse"/> and tests.</summary>
    internal DateExpression(string text, Regex regex)
    {
        Text = text;
        _regex = regex;
    }

    /// <summary>The expression as given (without the <c>^(?:…)$</c> wrapper).</summary>
    public string Text { get; }

    /// <summary>The compiled, wrapped expression.</summary>
    internal Regex Regex => _regex;

    /// <summary>Checks and compiles expression text. Never throws; problems are returned as errors.</summary>
    public static DateExpressionParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateExpressionParseResult.Failed(new DatePatternError(DatePatternErrorKind.Empty, 0));
        }

        if (text.Length > MaxLength)
        {
            return DateExpressionParseResult.Failed(new DatePatternError(DatePatternErrorKind.TooLong, MaxLength));
        }

        // Snapdate's own parser decides what is accepted: the allowed subset, within the limits (ExpressionGrammar).
        if (ExpressionGrammar.Check(text) is { } outside)
        {
            return DateExpressionParseResult.Failed(outside);
        }

        // The engine must agree. Compiled on its own first, so the text cannot close the ^(?:…)$ wrapper from inside
        // (the grammar already rejects that; this is the second opinion).
        var (_, alone) = Build(text);
        if (alone is not null)
        {
            return DateExpressionParseResult.Failed(alone with { Kind = DatePatternErrorKind.InvalidExpression });
        }

        var (regex, _) = Build("^(?:" + text + ")$");
        if (regex is null)
        {
            return DateExpressionParseResult.Failed(new DatePatternError(DatePatternErrorKind.InvalidExpression, 0));
        }

        var errors = CheckGroups(regex.GetGroupNames());
        return errors.Count == 0
            ? DateExpressionParseResult.Succeeded(new DateExpression(text, regex))
            : DateExpressionParseResult.Failed([.. errors]);
    }

    /// <summary>
    /// Matches the whole <paramref name="name"/> (without extension) and reads a valid, plausible date from it.
    /// Never throws; empty, too long or non-matching names, timeouts, impossible dates and implausible dates give false.
    /// </summary>
    public bool TryMatch(string? name, DateTime localNow, out FileNameDate date)
    {
        date = default;
        return DatePattern.TryMatchName(_regex, name, out var match) && DateCapture.TryRead(match, localNow, out date);
    }

    /// <summary>Like <see cref="TryMatch"/>, but also says why a fitting name gives no date. Never throws.</summary>
    public PatternCheck Check(string? name, DateTime localNow) =>
        DatePattern.TryMatchName(_regex, name, out var match) ? DateCapture.Check(match, localNow) : PatternCheck.NoMatch;

    public override string ToString() => Text;

    /// <summary>Compiles with the safe options; any failure becomes an error instead of an exception.</summary>
    private static (Regex? Regex, DatePatternError? Error) Build(string expression)
    {
        try
        {
            return (new Regex(expression, DatePattern.MatchOptions, DatePattern.MatchTimeout), null);
        }
        catch (RegexParseException e)
        {
            return (null, new DatePatternError(DatePatternErrorKind.InvalidSyntax, Math.Clamp(e.Offset, 0, expression.Length)));
        }
        catch (NotSupportedException)
        {
            // NonBacktracking rejects backreferences, lookarounds, atomic groups, conditionals and too large automata.
            return (null, new DatePatternError(DatePatternErrorKind.UnsupportedConstruct, 0));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Untrusted input: whatever else the engine throws (e.g. a stack overflow guard) must not crash the app.
            return (null, new DatePatternError(DatePatternErrorKind.InvalidExpression, 0));
        }
    }

    private static List<DatePatternError> CheckGroups(string[] names)
    {
        var errors = new List<DatePatternError>();
        foreach (var name in names)
        {
            // Numbered groups (always "0"; others only with (?-n) or (?<5>…)) are never read.
            if (name.Length > 0 && name.All(char.IsAsciiDigit))
            {
                continue;
            }

            if (!GroupNames.Contains(name, StringComparer.Ordinal))
            {
                errors.Add(new DatePatternError(DatePatternErrorKind.UnknownGroup, 0, name));
            }
        }

        bool Has(string group) => names.Contains(group, StringComparer.Ordinal);

        if (!Has(DateCapture.Year) && !Has(DateCapture.TwoDigitYear))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MissingYear, 0));
        }

        if (!Has(DateCapture.Month))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MissingMonth, 0));
        }

        if (!Has(DateCapture.Day))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MissingDay, 0));
        }

        if (Has(DateCapture.Hour) && !Has(DateCapture.Minute))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.HourWithoutMinute, 0, DateCapture.Hour));
        }

        if (Has(DateCapture.Minute) && !Has(DateCapture.Hour))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MinuteWithoutHour, 0, DateCapture.Minute));
        }

        if (Has(DateCapture.Second) && !Has(DateCapture.Minute))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.SecondWithoutMinute, 0, DateCapture.Second));
        }

        return errors;
    }
}
