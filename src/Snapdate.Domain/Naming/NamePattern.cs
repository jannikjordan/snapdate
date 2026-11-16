using System.Globalization;
using System.Text;
using Snapdate.Domain.Paths;
using Snapdate.Domain.Photos;

namespace Snapdate.Domain.Naming;

/// <summary>
/// A user-defined file name pattern such as <c>{YYYY}-{MM}-{DD} {HH}{mm}</c>.
/// Text outside braces is literal; <c>{{</c>/<c>}}</c> escape braces; <c>/</c> (or <c>\</c>)
/// creates sub-folders. The file extension is never part of the pattern.
/// </summary>
public sealed class NamePattern
{
    /// <summary>The pattern used when the user has not chosen one.</summary>
    public const string DefaultText = "{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}";

    private NamePattern(IReadOnlyList<PatternSegment> segments)
    {
        Segments = segments;
        Text = Format(segments);
    }

    public IReadOnlyList<PatternSegment> Segments { get; }

    /// <summary>Canonical text form (folder separators normalised to <c>/</c>).</summary>
    public string Text { get; }

    public static NamePattern Default { get; } = Parse(DefaultText).Pattern!;

    /// <summary>Builds a pattern from segments (e.g. from the visual builder).</summary>
    public static PatternParseResult FromSegments(IEnumerable<PatternSegment> segments) =>
        Parse(Format(segments));

    /// <summary>Parses pattern text. Never throws; problems are returned as errors.</summary>
    public static PatternParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PatternParseResult.Failed(new PatternError(PatternErrorKind.Empty, 0));
        }

        var errors = new List<PatternError>();
        var segments = new List<PatternSegment>();
        var literal = new StringBuilder();

        // Shape of the resulting path: tokens as 'x', literals as typed. Used for folder checks.
        var shape = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                segments.Add(new PatternSegment.Literal(literal.ToString()));
                literal.Clear();
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    literal.Append('{');
                    shape.Append('{');
                    i += 2;
                    continue;
                }

                var close = text.IndexOf('}', i + 1);
                if (close < 0)
                {
                    errors.Add(new PatternError(PatternErrorKind.UnclosedBrace, i));
                    break;
                }

                var name = text[(i + 1)..close];
                if (TokenCatalog.TryGet(name, out var kind))
                {
                    FlushLiteral();
                    segments.Add(new PatternSegment.Token(kind));
                    shape.Append('x');
                }
                else
                {
                    errors.Add(new PatternError(PatternErrorKind.UnknownPlaceholder, i, name));
                }

                i = close + 1;
                continue;
            }

            if (c == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}')
                {
                    literal.Append('}');
                    shape.Append('}');
                    i += 2;
                    continue;
                }

                errors.Add(new PatternError(PatternErrorKind.UnexpectedClosingBrace, i));
                i++;
                continue;
            }

            if (c == '\\')
            {
                c = '/';
            }

            if (c != '/' && FileNameSanitizer.IsInvalidChar(c))
            {
                errors.Add(new PatternError(PatternErrorKind.InvalidCharacter, i, c.ToString()));
                i++;
                continue;
            }

            literal.Append(c);
            shape.Append(c);
            i++;
        }

        FlushLiteral();

        if (errors.Count == 0)
        {
            CheckFolders(shape.ToString(), errors);
        }

        if (errors.Count == 0 && segments.Count == 0)
        {
            errors.Add(new PatternError(PatternErrorKind.Empty, 0));
        }

        return errors.Count > 0
            ? PatternParseResult.Failed([.. errors])
            : PatternParseResult.Succeeded(new NamePattern(segments));
    }

    /// <summary>
    /// Produces the relative target path without extension, using <c>/</c> between folders.
    /// Every segment is sanitised, so the result is always a safe relative path.
    /// </summary>
    public string Render(PhotoFacts facts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(culture);

        var raw = new StringBuilder();
        foreach (var segment in Segments)
        {
            raw.Append(segment switch
            {
                PatternSegment.Literal literal => literal.Text,
                PatternSegment.Token token => FileNameSanitizer.SanitizeValue(Value(token.Kind, facts, culture)),
                _ => string.Empty,
            });
        }

        var parts = raw.ToString().Split('/');
        if (string.IsNullOrWhiteSpace(parts[^1]))
        {
            // e.g. pattern "{camera}" for a photo without camera data: keep the original name.
            parts[^1] = facts.OriginalName;
        }

        return string.Join('/', parts.Select(FileNameSanitizer.SanitizeSegment));
    }

    public override string ToString() => Text;

    private static string Value(TokenKind kind, PhotoFacts facts, CultureInfo culture)
    {
        var d = facts.Date.Value;
        var inv = CultureInfo.InvariantCulture;
        return kind switch
        {
            TokenKind.Year4 => d.Year.ToString("0000", inv),
            TokenKind.Year2 => (d.Year % 100).ToString("00", inv),
            TokenKind.Month2 => d.Month.ToString("00", inv),
            TokenKind.Month1 => d.Month.ToString(inv),
            TokenKind.MonthNameShort => ShortName(culture.DateTimeFormat.GetMonthName(d.Month)),
            TokenKind.MonthNameLong => culture.DateTimeFormat.GetMonthName(d.Month),
            TokenKind.Day2 => d.Day.ToString("00", inv),
            TokenKind.Day1 => d.Day.ToString(inv),
            TokenKind.WeekdayShort => culture.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.'),
            TokenKind.WeekdayLong => culture.DateTimeFormat.GetDayName(d.DayOfWeek),
            TokenKind.Hour2 => d.Hour.ToString("00", inv),
            TokenKind.Hour1 => d.Hour.ToString(inv),
            TokenKind.Minute2 => d.Minute.ToString("00", inv),
            TokenKind.Minute1 => d.Minute.ToString(inv),
            TokenKind.Second2 => d.Second.ToString("00", inv),
            TokenKind.Second1 => d.Second.ToString(inv),
            TokenKind.OriginalName => facts.OriginalName,
            TokenKind.Camera => facts.CameraModel ?? string.Empty,
            TokenKind.Counter => facts.Counter.ToString("000", inv),
            _ => string.Empty,
        };
    }

    // Culture abbreviations vary ("Sept.", "Juni"); three letters of the full name are consistent.
    private static string ShortName(string fullName) =>
        fullName.Length <= 3 ? fullName : fullName[..3];

    private static void CheckFolders(string shape, List<PatternError> errors)
    {
        for (var i = 0; i < shape.Length; i++)
        {
            if (shape[i] != '/')
            {
                continue;
            }

            var previousIsBoundary = i == 0 || shape[i - 1] == '/';
            var isLast = i == shape.Length - 1;
            if (previousIsBoundary || isLast)
            {
                errors.Add(new PatternError(PatternErrorKind.EmptyFolderName, i));
                return;
            }
        }
    }

    private static string Format(IEnumerable<PatternSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case PatternSegment.Literal literal:
                    builder.Append(literal.Text
                        .Replace("{", "{{", StringComparison.Ordinal)
                        .Replace("}", "}}", StringComparison.Ordinal)
                        .Replace('\\', '/'));
                    break;
                case PatternSegment.Token token:
                    builder.Append('{').Append(TokenCatalog.NameOf(token.Kind)).Append('}');
                    break;
            }
        }

        return builder.ToString();
    }
}
