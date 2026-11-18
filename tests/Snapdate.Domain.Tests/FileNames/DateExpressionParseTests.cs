using System.Diagnostics;
using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

/// <summary>
/// <see cref="DateExpression.Parse(string?)"/>: user-written regular expressions are untrusted input. Every problem
/// must come back as an error, never as an exception.
/// </summary>
public sealed class DateExpressionParseTests
{
    [Theory]
    [InlineData(@"^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$")]
    [InlineData(@"IMG-(?<year>[0-9]{4})(?<month>[0-9]{2})(?<day>[0-9]{2})-WA.*")]
    [InlineData(@"(?<day>\d{1,2})\.(?<month>\d{1,2})\.(?<yy>\d{2})")]
    [InlineData(@"(?<year>\d{4})-(?<month>\d\d)-(?<day>\d\d) (?<hour>\d\d)\.(?<minute>\d\d)\.(?<second>\d\d).*")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_(?<hour>\d{2})(?<minute>\d{2})")]
    [InlineData(@"(?:PXL|IMG)_(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_\d+")]
    [InlineData(@"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})|(?<day>\d{2})\.(?<month>\d{2})\.(?<year>\d{4})")]
    [InlineData(@"\bphoto\b (?<year>\d{4})(?<month>\d{2})(?<day>\d{2})")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?-n)(x)?")]
    public void Valid_expressions_parse(string text)
    {
        var result = DateExpression.Parse(text);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
        Assert.Empty(result.Errors);
        Assert.Equal(text, result.Expression!.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_expression_is_an_error(string? text)
    {
        AssertSingleError(text, DatePatternErrorKind.Empty);
    }

    [Fact]
    public void Expression_longer_than_300_characters_is_an_error()
    {
        var text = Valid + new string('x', DateExpression.MaxLength - Valid.Length + 1);

        var error = AssertSingleError(text, DatePatternErrorKind.TooLong);
        Assert.Equal(DateExpression.MaxLength, error.Position);
    }

    [Fact]
    public void Expression_of_exactly_300_characters_is_valid()
    {
        var text = Valid + new string('x', DateExpression.MaxLength - Valid.Length);

        Assert.True(DateExpression.Parse(text).IsValid);
    }

    [Theory]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\1", "backreference")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\k<year>", "named backreference")]
    [InlineData(@"(?=IMG)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", "lookahead")]
    [InlineData(@"(?!IMG)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", "negative lookahead")]
    [InlineData(@"(?<=x)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", "lookbehind")]
    [InlineData(@"(?<!x)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", "negative lookbehind")]
    [InlineData(@"(?>(?<year>\d{4}))(?<month>\d{2})(?<day>\d{2})", "atomic group")]
    [InlineData(@"(?(x)a|b)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", "conditional")]
    [InlineData(@"\G(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", "contiguous match")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<-year>x)", "balancing group")]
    [InlineData(@"((((?<year>\d{4})(?<month>\d{2})(?<day>\d{2})){100}){100}){100}", "too complex overall")]
    public void Constructs_the_safe_matcher_cannot_run_are_an_error(string text, string construct)
    {
        var result = DateExpression.Parse(text);

        Assert.False(result.IsValid, construct);
        Assert.Contains(result.Errors, e => e.Kind is DatePatternErrorKind.UnsupportedConstruct or DatePatternErrorKind.InvalidSyntax);
    }

    [Theory]
    [InlineData(@"(?=IMG)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})")]
    [InlineData(@"(?>(?<year>\d{4}))(?<month>\d{2})(?<day>\d{2})")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\k<year>")]
    public void Unsupported_constructs_have_their_own_error_kind(string text)
    {
        AssertSingleError(text, DatePatternErrorKind.UnsupportedConstruct);
    }

    [Theory]
    [InlineData(@"(?<year>\d{4}(?<month>\d{2})(?<day>\d{2})", 41)]
    [InlineData(@"(?<year>\d{4}))(?<month>\d{2})(?<day>\d{2})", 14)]
    [InlineData(@"(?<year>\d{4})(?<month>[0-9{2})(?<day>\d{2})", 44)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\", 43)]
    [InlineData(@"*(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", 0)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\q", 43)]
    public void Syntax_errors_say_where_the_problem_is(string text, int position)
    {
        var error = AssertSingleError(text, DatePatternErrorKind.InvalidSyntax);

        Assert.InRange(error.Position, Math.Max(0, position - 2), position + 1);
        Assert.InRange(error.Position, 0, text.Length);
    }

    [Fact]
    public void Closing_the_wrapper_from_inside_is_rejected()
    {
        // Would otherwise turn ^(?:…)$ into "^(?:a)|(?:…)$", which no longer has to match the whole name.
        var result = DateExpression.Parse(@"a)|(?:(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})");

        Assert.False(result.IsValid);
        Assert.Equal(DatePatternErrorKind.InvalidSyntax, result.Errors[0].Kind);
    }

    [Theory]
    [InlineData(@"(?x)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2}) #")]
    [InlineData("(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})\n")]
    [InlineData("(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})\0")]
    public void Comments_and_control_characters_that_could_swallow_the_wrapper_are_rejected(string text)
    {
        Assert.False(DateExpression.Parse(text).IsValid);
    }

    [Theory]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{101}", "{101}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{1,101}", "{1,101}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{101,}", "{101,}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{2147483648}", "{2147483648}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{99999999999999999999}", "{99999999999999999999}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\\{500}", "{500}")]
    public void Huge_counted_repetitions_are_an_error(string text, string repetition)
    {
        var error = AssertSingleError(text, DatePatternErrorKind.RepetitionTooLarge);

        Assert.Equal(repetition, error.Detail);
        Assert.Equal(text.LastIndexOf('{'), error.Position);
    }

    [Theory]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{100}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{0,100}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{100,}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\{500}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\p{L}*")]
    public void Repetitions_up_to_100_and_literal_braces_are_fine(string text)
    {
        var result = DateExpression.Parse(text);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    [Theory]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<counter>\d+)", "counter")]
    [InlineData(@"(?<Year>\d{4})(?<month>\d{2})(?<day>\d{2})", "Year")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?'tag'x)", "tag")]
    public void Unknown_group_names_are_an_error(string text, string name)
    {
        var result = DateExpression.Parse(text);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Kind == DatePatternErrorKind.UnknownGroup && e.Detail == name);
    }

    [Fact]
    public void Numbered_groups_are_ignored()
    {
        Assert.True(DateExpression.Parse(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<5>x)?").IsValid);
    }

    [Theory]
    [InlineData(@"(?<month>\d{2})(?<day>\d{2})", DatePatternErrorKind.MissingYear)]
    [InlineData(@"(?<year>\d{4})(?<day>\d{2})", DatePatternErrorKind.MissingMonth)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})", DatePatternErrorKind.MissingDay)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_(?<hour>\d{2})", DatePatternErrorKind.HourWithoutMinute)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_(?<minute>\d{2})", DatePatternErrorKind.MinuteWithoutHour)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_(?<second>\d{2})", DatePatternErrorKind.SecondWithoutMinute)]
    public void Required_and_dependent_groups_are_checked(string text, DatePatternErrorKind kind)
    {
        Assert.Contains(DateExpression.Parse(text).Errors, e => e.Kind == kind);
    }

    [Fact]
    public void An_expression_without_any_group_lists_every_missing_part()
    {
        var kinds = DateExpression.Parse(@"IMG_\d+").Errors.Select(e => e.Kind);

        Assert.Equal([DatePatternErrorKind.MissingYear, DatePatternErrorKind.MissingMonth, DatePatternErrorKind.MissingDay], kinds);
    }

    [Fact]
    public void Every_error_kind_for_expressions_is_covered()
    {
        string?[] samples =
        [
            "",
            Valid + new string('x', DateExpression.MaxLength),
            Valid + "\n",
            Valid + "(",
            Valid + "(?=x)",
            Valid + "x{101}",
            Valid + "(?<foo>x)",
            "(?<month>1)(?<day>1)",
            "(?<year>1)(?<day>1)",
            "(?<year>1)(?<month>1)",
            Valid + "(?<hour>1)",
            Valid + "(?<minute>1)",
            Valid + "(?<second>1)",
        ];

        var seen = samples.SelectMany(sample => DateExpression.Parse(sample).Errors).Select(error => error.Kind).ToHashSet();

        // Placeholder problems only exist in token patterns.
        DatePatternErrorKind[] patternOnly =
        [
            DatePatternErrorKind.UnclosedBrace,
            DatePatternErrorKind.UnexpectedClosingBrace,
            DatePatternErrorKind.UnknownToken,
            DatePatternErrorKind.UnsupportedToken,
            DatePatternErrorKind.DuplicateToken,
            DatePatternErrorKind.AmbiguousNeighbours,
        ];
        Assert.Equal(Enum.GetValues<DatePatternErrorKind>().Except(patternOnly).ToHashSet(), seen);
    }

    [Theory]
    [MemberData(nameof(HostileExpressions))]
    public void Hostile_expressions_never_throw_and_never_hang(string text)
    {
        var watch = Stopwatch.StartNew();

        var result = DateExpression.Parse(text);
        result.Expression?.TryMatch(new string('a', DatePattern.MaxNameLength), new DateTime(2026, 11, 18), out _);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{watch.Elapsed} for {text}");
    }

    public static TheoryData<string> HostileExpressions() =>
    [
        new string('(', 150) + new string(')', 150),
        new string('(', 300),
        new string(')', 300),
        new string('[', 300),
        new string('\\', 300),
        string.Concat(Enumerable.Repeat("(a+)+", 60)),
        string.Concat(Enumerable.Repeat(".{100}", 50)),
        string.Concat(Enumerable.Repeat("(?:", 99)) + "a" + string.Concat(Enumerable.Repeat("){100}", 33)),
        @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})" + string.Concat(Enumerable.Repeat("|.*", 80)),
        @"\p{",
        @"\p{Nope}",
        @"(?<",
        @"(?<>x)",
        @"(?<0>x)",
        @"(?<a-b>x)",
        @"(?#",
        @"[z-a]",
        @"x**",
        @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<year>\d{4})",
        "\uD800(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})",
    ];

    private const string Valid = @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})";

    private static DatePatternError AssertSingleError(string? text, DatePatternErrorKind kind)
    {
        var result = DateExpression.Parse(text);

        Assert.False(result.IsValid);
        Assert.Null(result.Expression);
        var error = Assert.Single(result.Errors);
        Assert.Equal(kind, error.Kind);
        return error;
    }
}
