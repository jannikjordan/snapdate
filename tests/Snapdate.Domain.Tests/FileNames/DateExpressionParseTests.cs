using System.Diagnostics;
using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

/// <summary>
/// <see cref="DateExpression.Parse(string?)"/>: user-written regular expressions are untrusted input. Only an allowed
/// subset within limits is accepted; every problem must come back as an error, never as an exception.
/// </summary>
public sealed class DateExpressionParseTests
{
    private const string Valid = @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})";

    [Theory]
    [InlineData(@"^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$")]
    [InlineData(@"IMG-(?<year>[0-9]{4})(?<month>[0-9]{2})(?<day>[0-9]{2})-WA.*")]
    [InlineData(@"(?<day>\d{1,2})\.(?<month>\d{1,2})\.(?<yy>\d{2})")]
    [InlineData(@"(?<year>\d{4})-(?<month>\d\d)-(?<day>\d\d) (?<hour>\d\d)\.(?<minute>\d\d)\.(?<second>\d\d).*")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_(?<hour>\d{2})(?<minute>\d{2})")]
    [InlineData(@"(?:PXL|IMG)_(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})_\d+")]
    [InlineData(@"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})|(?<day>\d{2})\.(?<month>\d{2})\.(?<year>\d{4})")]
    [InlineData(@"photo (?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(x)?")]
    [InlineData(@"Screenshot[ _](?<year>\d{4})-?(?<month>\d{2})-?(?<day>\d{2}).*")]
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

    // ---------- The allowed subset ----------

    [Theory]
    [InlineData(@"\1", @"\1")]
    [InlineData(@"\k<year>", @"\k")]
    [InlineData(@"\b", @"\b")]
    [InlineData(@"\B", @"\B")]
    [InlineData(@"\G", @"\G")]
    [InlineData(@"\p{L}", @"\p")]
    [InlineData(@"\n", @"\n")]
    [InlineData(@"\x41", @"\x")]
    [InlineData(@"\_", @"\_")]
    [InlineData(@"\D", @"\D")]
    [InlineData("(?=x)", "(?=")]
    [InlineData("(?!x)", "(?!")]
    [InlineData("(?<=x)", "(?<")]
    [InlineData("(?<!x)", "(?<")]
    [InlineData("(?>x)", "(?>")]
    [InlineData("(?(x)a|b)", "(?(")]
    [InlineData("(?#note)", "(?#")]
    [InlineData("(?i)", "(?i")]
    [InlineData("(?x)", "(?x")]
    [InlineData("(?i:x)", "(?i")]
    [InlineData("(?'tag'x)", "(?'")]
    [InlineData("{", "{")]
    [InlineData("x{", "{")]
    [InlineData("x{a}", "{")]
    [InlineData("}", "}")]
    [InlineData("]", "]")]
    public void Anything_outside_the_subset_is_named_in_the_error(string tail, string detail)
    {
        var error = AssertSingleError(Valid + tail, DatePatternErrorKind.UnsupportedConstruct);

        Assert.Equal(detail, error.Detail);
        Assert.Equal(Valid.Length + tail.IndexOf(detail[0], StringComparison.Ordinal), error.Position);
    }

    [Theory]
    [InlineData(@"[\-[x]", "[")]
    [InlineData("[a-z-[aeiou]]", "[")]
    [InlineData("[[:alpha:]]", "[")]
    [InlineData("[]a]", "[]")]
    [InlineData("[^]a]", "[^]")]
    [InlineData(@"[\p{L}]", @"\p")]
    [InlineData(@"[\b]", @"\b")]
    [InlineData("[z-a]", "z-a")]
    [InlineData(@"[\d-z]", @"\d-z")]
    [InlineData(@"[a-\w]", @"a-\w")]
    public void Character_classes_are_simple(string tail, string detail)
    {
        var error = AssertSingleError(Valid + tail, DatePatternErrorKind.UnsupportedConstruct);

        Assert.Equal(detail, error.Detail);
    }

    [Theory]
    [InlineData(@"[\]\-\[x]")]
    [InlineData("[^_]")]
    [InlineData("[a-]")]
    [InlineData("[-a]")]
    [InlineData(@"[\d\s\w.,;]")]
    [InlineData("[(){}*+?|$^]")]
    [InlineData(@"\{3\}\]\[\(\)\.\*\+\?\|\^\$\\\-\ ")]
    public void Escapes_and_simple_classes_are_allowed(string tail)
    {
        var result = DateExpression.Parse(Valid + tail);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    [Theory]
    [InlineData(@"(?<year>\d{4}(?<month>\d{2})(?<day>\d{2})", 41)]
    [InlineData(@"(?<year>\d{4}))(?<month>\d{2})(?<day>\d{2})", 15)]
    [InlineData(@"(?<year>\d{4})(?<month>[0-9{2})(?<day>\d{2})", 44)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})\", 43)]
    [InlineData(@"*(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", 1)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x**", 45)]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{3,2}", 44)]
    public void Syntax_errors_say_where_the_problem_is(string text, int position)
    {
        var error = AssertSingleError(text, DatePatternErrorKind.InvalidSyntax);

        Assert.Equal(position, error.Position);
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
    [InlineData("(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})\n")]
    [InlineData("(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})\0")]
    [InlineData("(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})[\t]")]
    public void Control_characters_are_rejected(string text)
    {
        AssertSingleError(text, DatePatternErrorKind.InvalidExpression);
    }

    [Theory]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{21}", "{21}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{1,21}", "{1,21}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{21,}", "{21,}")]
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
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{20}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{0,20}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{20,}")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})x{0,20}?")]
    public void Repetitions_up_to_20_are_fine(string text)
    {
        var result = DateExpression.Parse(text);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    [Theory]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<counter>\d+)", "counter")]
    [InlineData(@"(?<Year>\d{4})(?<month>\d{2})(?<day>\d{2})", "Year")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<5>x)", "5")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<a-b>x)", "a-b")]
    public void Only_date_parts_can_be_named(string text, string name)
    {
        var error = AssertSingleError(text, DatePatternErrorKind.UnknownGroup);

        Assert.Equal(name, error.Detail);
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
            Valid + "x{21}",
            Valid + "(?:x*)+",
            Valid + string.Concat(Enumerable.Repeat(".*", 9)),
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

    // ---------- Variable width must not be repeated ----------

    [Theory]
    [InlineData("(?:x\\d+)+", "+")]
    [InlineData("(?:x\\d?)*", "*")]
    [InlineData("(x.*)?", "?")]
    [InlineData("(x.*)??", "??")]
    [InlineData("(?:x\\d{1,2}){2}", "{2}")]
    [InlineData("(?:x\\d{1,2}){0,2}?", "{0,2}?")]
    [InlineData("(?:(?:x\\d*))+", "+")]
    [InlineData("(?:a|b*)+", "+")]
    [InlineData("(?:|.){3}", "{3}")]
    [InlineData("(?:a|)*", "*")]
    [InlineData("(?:a|bb)+", "+")]
    [InlineData("(?:(?:.|..)a){20}", "{20}")]
    [InlineData("(?:x(?:|.))?", "?")]
    public void Nothing_whose_width_varies_may_be_repeated(string tail, string quantifier)
    {
        var text = Valid + tail;

        var error = AssertSingleError(text, DatePatternErrorKind.NestedQuantifier);

        Assert.Equal(quantifier, error.Detail);
        Assert.Equal(text.Length - quantifier.Length, error.Position);
    }

    [Theory]
    [InlineData("(?:ab|cd){3}")]
    [InlineData("(?:_\\d{2}\\d{2})?")]
    [InlineData("(?:[-_](?:WA|MP)\\d{4})?")]
    [InlineData("(?:|.)")]
    [InlineData("(?:a|bb)")]
    [InlineData("(?:\\d{2}){20}")]
    public void Fixed_width_parts_may_be_repeated_and_variable_ones_may_stand_alone(string tail)
    {
        var result = DateExpression.Parse(Valid + tail);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    [Theory]
    [InlineData("^*")]
    [InlineData("$+")]
    [InlineData("()?")]
    [InlineData("(?:^){3}")]
    public void Repeating_nothing_is_rejected(string tail)
    {
        AssertSingleError(Valid + tail, DatePatternErrorKind.UnsupportedConstruct);
    }

    [Theory]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?:_(?<hour>\d{2})(?<minute>\d{2}))?")]
    [InlineData(@"(?<year>\d{4})[-_ ]?(?<month>\d{2})[-_ ]?(?<day>\d{2})")]
    [InlineData(@"(?:IMG|VID|PXL)[-_](?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?:[-_](?:WA|MP)\d{4})?")]
    [InlineData(@".*(?<year>\d{4})[-_.]?(?<month>\d{2})[-_.]?(?<day>\d{2})[-_ T]?(?<hour>\d{2})[-_.:]?(?<minute>\d{2})[-_.:]?(?<second>\d{2}).*")]
    [InlineData(@"(?:WhatsApp|Signal) .* (?<year>\d{4})-(?<month>\d{1,2})-(?<day>\d{1,2}) .* (?<hour>\d{1,2})\.(?<minute>\d{2})\.(?<second>\d{2}).*")]
    [InlineData(@"(?:ab|ba)*(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?:x|y){3}")]
    [InlineData(@"[^_]*_(?<day>\d{1,2})\.(?<month>\d{1,2})\.(?<yy>\d{2})(?: \(\d\))?")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})[\[\]x]*\{3\}[a-z_-]+")]
    [InlineData(@"Screenshot[ _](?<year>\d{4})-?(?<month>\d{2})-?(?<day>\d{2}).*")]
    [InlineData(@"^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$")]
    [InlineData(@"WhatsApp (?:Image|Video|Bild) (?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}) (?:at|um) (?<hour>\d{2})\.(?<minute>\d{2})\.(?<second>\d{2}).*")]
    public void Realistic_expressions_stay_within_the_limits(string text)
    {
        var result = DateExpression.Parse(text);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
    }

    // ---------- Budget ----------

    [Theory]
    [InlineData(31, false)]
    [InlineData(30, true)]
    public void At_most_30_quantifiers_are_allowed(int count, bool valid)
    {
        // Date groups use 3 fixed counts; "a?" adds one each (no unbounded ones, so no complexity cost).
        var text = Valid + string.Concat(Enumerable.Repeat("a?", count - 3));

        Assert.Equal(valid, DateExpression.Parse(text).IsValid);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(8, true)]
    public void At_most_8_unbounded_quantifiers_are_allowed(int count, bool valid)
    {
        var text = string.Concat(Enumerable.Repeat(".*a", count)) + "(?<year>....)(?<month>..)(?<day>..)";

        var result = DateExpression.Parse(text);

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal(DatePatternErrorKind.TooComplex, Assert.Single(result.Errors).Kind);
        }
    }

    [Theory]
    [InlineData("[a-z]*.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,11}", true)] // 1 × (8 + 1 + 231) = 240
    [InlineData("[a-z]*.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,12}", false)] // 241
    [InlineData(".*.*.*.*.*.*.*.*.?.?.?.?.?.?.?.?.?.?.?.?.?.?", true)] // 8 × (8 + 8 + 14) = 240
    [InlineData(".*.*.*.*.*.*.*.*.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?", false)] // 8 × 31 = 248
    [InlineData("[a-z]*(?:abcdefghij){20}", true)] // 1 × (8 + 1 + 200) = 209: a fixed-count group counts its content each time
    [InlineData("[a-z]*(?:abcdefghijkl){20}", false)] // 1 × (8 + 1 + 240) = 249
    public void Unbounded_quantifiers_times_positions_have_a_budget(string tail, bool valid)
    {
        var result = DateExpression.Parse(Valid + tail);

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal(DatePatternErrorKind.TooComplex, Assert.Single(result.Errors).Kind);
        }
    }

    [Fact]
    public void An_alternation_counts_every_branch()
    {
        string Text(int tail) => Valid + "[a-z]*(?:" + new string('a', 100) + "|" + new string('b', 100) + ")" + new string('a', tail);

        Assert.True(DateExpression.Parse(Text(31)).IsValid); // 1 × (8 + 1 + 200 + 31) = 240
        Assert.Equal(DatePatternErrorKind.TooComplex, Assert.Single(DateExpression.Parse(Text(32)).Errors).Kind);
    }

    [Fact]
    public void Without_unbounded_quantifiers_there_is_no_complexity_cost()
    {
        var text = Valid + string.Concat(Enumerable.Repeat(".{0,20}", 27)); // 30 quantifiers, 548 positions

        Assert.True(DateExpression.Parse(text).IsValid);
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
        new string('|', 300),
        string.Concat(Enumerable.Repeat("(?:", 99)) + "a" + new string(')', 99),
        string.Concat(Enumerable.Repeat("(a+)+", 60)),
        string.Concat(Enumerable.Repeat(".{100}", 50)),
        string.Concat(Enumerable.Repeat("(?:", 99)) + "a" + string.Concat(Enumerable.Repeat("){100}", 33)),
        Valid + string.Concat(Enumerable.Repeat("|.*", 80)),
        @"\p{",
        @"\p{Nope}",
        @"(?<",
        @"(?<>x)",
        @"(?<0>x)",
        @"(?<a-b>x)",
        @"(?#",
        @"[z-a]",
        @"[",
        @"[^",
        @"[a-",
        @"[\",
        @"x**",
        @"x{",
        @"x{1,",
        Valid + Valid,
        "\uD800" + Valid,
    ];

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
