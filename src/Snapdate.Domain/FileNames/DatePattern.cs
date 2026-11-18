using System.Text;
using System.Text.RegularExpressions;
using Snapdate.Domain.Naming;

namespace Snapdate.Domain.FileNames;

/// <summary>
/// A pattern that reads a date from a file name (without extension), e.g. <c>IMG-{YYYY}{MM}{DD}-WA*</c> —
/// the name builder in reverse. Placeholders <c>{YYYY} {YY} {MM} {M} {DD} {D} {HH} {H} {mm} {m} {ss} {s}</c>;
/// <c>*</c> = any text, <c>?</c> = exactly one character; everything else is literal and case-insensitive;
/// <c>{{</c>/<c>}}</c> escape braces. The pattern must match the whole name.
/// </summary>
public sealed class DatePattern : IFileNameDateMatcher
{
    /// <summary>Maximum pattern length in characters.</summary>
    public const int MaxLength = 200;

    /// <summary>Longer names never match.</summary>
    public const int MaxNameLength = 260;

    /// <summary>Options for every generated expression: linear time, no culture-dependent behaviour.</summary>
    internal const RegexOptions MatchOptions =
        RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    /// <summary>Upper bound for a single match; a timeout counts as no match.</summary>
    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private readonly Regex _regex;

    private DatePattern(string text, Regex regex)
    {
        Text = text;
        _regex = regex;
    }

    private enum Field
    {
        Year,
        Month,
        Day,
        Hour,
        Minute,
        Second,
    }

    /// <summary>The pattern text as given.</summary>
    public string Text { get; }

    /// <summary>Parses pattern text. Never throws; problems are returned as errors.</summary>
    public static DatePatternParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DatePatternParseResult.Failed(new DatePatternError(DatePatternErrorKind.Empty, 0));
        }

        if (text.Length > MaxLength)
        {
            return DatePatternParseResult.Failed(new DatePatternError(DatePatternErrorKind.TooLong, MaxLength));
        }

        var errors = new List<DatePatternError>();
        var elements = Tokenize(text, errors);
        if (errors.Count == 0)
        {
            CheckFields(elements, errors);
            CheckNeighbours(elements, errors);
        }

        return errors.Count > 0
            ? DatePatternParseResult.Failed([.. errors])
            : DatePatternParseResult.Succeeded(new DatePattern(text, new Regex(ToRegex(elements), MatchOptions, MatchTimeout)));
    }

    /// <summary>
    /// Matches the whole <paramref name="name"/> (without extension) and reads a valid, plausible date from it.
    /// Never throws; empty, too long or non-matching names, impossible dates and dates outside
    /// <see cref="Photos.DatePlausibility"/> give false.
    /// </summary>
    public bool TryMatch(string? name, DateTime localNow, out FileNameDate date)
    {
        date = default;
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        Match match;
        try
        {
            match = _regex.Match(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        return match.Success && DateCapture.TryRead(match, localNow, out date);
    }

    public override string ToString() => Text;

    private static List<Element> Tokenize(string text, List<DatePatternError> errors)
    {
        var elements = new List<Element>();
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                elements.Add(new Element.Literal(literal.ToString()));
                literal.Clear();
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
            {
                case '{' when i + 1 < text.Length && text[i + 1] == '{':
                case '}' when i + 1 < text.Length && text[i + 1] == '}':
                    literal.Append(c);
                    i += 2;
                    continue;
                case '{':
                    var close = text.IndexOf('}', i + 1);
                    if (close < 0)
                    {
                        errors.Add(new DatePatternError(DatePatternErrorKind.UnclosedBrace, i));
                        return elements;
                    }

                    var name = text[(i + 1)..close];
                    if (!TokenCatalog.TryGet(name, out var kind))
                    {
                        errors.Add(new DatePatternError(DatePatternErrorKind.UnknownToken, i, name));
                    }
                    else if (FieldOf(kind) is not { } field)
                    {
                        errors.Add(new DatePatternError(DatePatternErrorKind.UnsupportedToken, i, name));
                    }
                    else
                    {
                        FlushLiteral();
                        elements.Add(new Element.Token(kind, field, name, i));
                    }

                    i = close + 1;
                    continue;
                case '}':
                    errors.Add(new DatePatternError(DatePatternErrorKind.UnexpectedClosingBrace, i));
                    i++;
                    continue;
                case '*' or '?':
                    FlushLiteral();
                    elements.Add(new Element.Wildcard(c, i));
                    i++;
                    continue;
                default:
                    literal.Append(c);
                    i++;
                    continue;
            }
        }

        FlushLiteral();
        return elements;
    }

    private static void CheckFields(List<Element> elements, List<DatePatternError> errors)
    {
        var found = new Dictionary<Field, Element.Token>();
        foreach (var token in elements.OfType<Element.Token>())
        {
            if (!found.TryAdd(token.Field, token))
            {
                errors.Add(new DatePatternError(DatePatternErrorKind.DuplicateToken, token.Position, token.Name));
            }
        }

        if (!found.ContainsKey(Field.Year))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MissingYear, 0));
        }

        if (!found.ContainsKey(Field.Month))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MissingMonth, 0));
        }

        if (!found.ContainsKey(Field.Day))
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MissingDay, 0));
        }

        found.TryGetValue(Field.Hour, out var hour);
        found.TryGetValue(Field.Minute, out var minute);
        found.TryGetValue(Field.Second, out var second);
        if (hour is not null && minute is null)
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.HourWithoutMinute, hour.Position, hour.Name));
        }

        if (minute is not null && hour is null)
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.MinuteWithoutHour, minute.Position, minute.Name));
        }

        if (second is not null && minute is null)
        {
            errors.Add(new DatePatternError(DatePatternErrorKind.SecondWithoutMinute, second.Position, second.Name));
        }
    }

    private static void CheckNeighbours(List<Element> elements, List<DatePatternError> errors)
    {
        for (var i = 1; i < elements.Count; i++)
        {
            switch (elements[i - 1], elements[i])
            {
                case (Element.Wildcard wildcard, Element.Token { IsVariableWidth: true } token):
                    errors.Add(new DatePatternError(DatePatternErrorKind.AmbiguousNeighbours, wildcard.Position, token.Name));
                    break;
                case (Element.Token { IsVariableWidth: true } token, Element.Wildcard wildcard):
                    errors.Add(new DatePatternError(DatePatternErrorKind.AmbiguousNeighbours, wildcard.Position, token.Name));
                    break;
                case (Element.Token { IsVariableWidth: true }, Element.Token { IsVariableWidth: true } token):
                    errors.Add(new DatePatternError(DatePatternErrorKind.AmbiguousNeighbours, token.Position, token.Name));
                    break;
            }
        }
    }

    private static string ToRegex(List<Element> elements)
    {
        var regex = new StringBuilder("^");
        foreach (var element in elements)
        {
            regex.Append(element switch
            {
                Element.Literal literal => Regex.Escape(literal.Text),
                Element.Wildcard { Character: '*' } => ".*?",
                Element.Wildcard => ".",
                Element.Token token => Group(token.Kind),
                _ => string.Empty,
            });
        }

        return regex.Append('$').ToString();
    }

    private static string Group(TokenKind kind) => kind switch
    {
        TokenKind.Year4 => $"(?<{DateCapture.Year}>[0-9]{{4}})",
        TokenKind.Year2 => $"(?<{DateCapture.TwoDigitYear}>[0-9]{{2}})",
        TokenKind.Month2 => $"(?<{DateCapture.Month}>[0-9]{{2}})",
        TokenKind.Month1 => $"(?<{DateCapture.Month}>[0-9]{{1,2}})",
        TokenKind.Day2 => $"(?<{DateCapture.Day}>[0-9]{{2}})",
        TokenKind.Day1 => $"(?<{DateCapture.Day}>[0-9]{{1,2}})",
        TokenKind.Hour2 => $"(?<{DateCapture.Hour}>[0-9]{{2}})",
        TokenKind.Hour1 => $"(?<{DateCapture.Hour}>[0-9]{{1,2}})",
        TokenKind.Minute2 => $"(?<{DateCapture.Minute}>[0-9]{{2}})",
        TokenKind.Minute1 => $"(?<{DateCapture.Minute}>[0-9]{{1,2}})",
        TokenKind.Second2 => $"(?<{DateCapture.Second}>[0-9]{{2}})",
        TokenKind.Second1 => $"(?<{DateCapture.Second}>[0-9]{{1,2}})",
        _ => string.Empty,
    };

    private static Field? FieldOf(TokenKind kind) => kind switch
    {
        TokenKind.Year4 or TokenKind.Year2 => Field.Year,
        TokenKind.Month2 or TokenKind.Month1 => Field.Month,
        TokenKind.Day2 or TokenKind.Day1 => Field.Day,
        TokenKind.Hour2 or TokenKind.Hour1 => Field.Hour,
        TokenKind.Minute2 or TokenKind.Minute1 => Field.Minute,
        TokenKind.Second2 or TokenKind.Second1 => Field.Second,
        _ => null,
    };

    private abstract record Element
    {
        public sealed record Literal(string Text) : Element;

        public sealed record Wildcard(char Character, int Position) : Element;

        public sealed record Token(TokenKind Kind, Field Field, string Name, int Position) : Element
        {
            /// <summary>True for one-or-two-digit tokens such as <c>{M}</c>.</summary>
            public bool IsVariableWidth =>
                Kind is TokenKind.Month1 or TokenKind.Day1 or TokenKind.Hour1 or TokenKind.Minute1 or TokenKind.Second1;
        }
    }
}
