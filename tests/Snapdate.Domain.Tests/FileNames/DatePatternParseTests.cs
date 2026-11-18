using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

public sealed class DatePatternParseTests
{
    [Theory]
    [InlineData("IMG-{YYYY}{MM}{DD}-WA*")]
    [InlineData("VID-{YYYY}{MM}{DD}-WA*")]
    [InlineData("WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*")]
    [InlineData("{YYYY}{MM}{DD}_{HH}{mm}{ss}")]
    [InlineData("{D}.{M}.{YY}")]
    [InlineData("{YYYY}-{MM}-{DD} {H}h{m}")]
    [InlineData("*_{YYYY}{MM}{DD}")]
    [InlineData("{YYYY}{MM}{DD}?{HH}{mm}")]
    [InlineData("{{{YYYY}}}-{MM}-{DD}")]
    public void Valid_patterns_parse(string text)
    {
        var result = DatePattern.Parse(text);

        Assert.True(result.IsValid, string.Join(", ", result.Errors));
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Pattern);
        Assert.Equal(text, result.Pattern!.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_pattern_is_an_error(string? text)
    {
        AssertSingleError(text, DatePatternErrorKind.Empty, 0);
    }

    [Fact]
    public void Pattern_longer_than_200_characters_is_an_error()
    {
        var text = "{YYYY}{MM}{DD}" + new string('x', DatePattern.MaxLength - 13);

        AssertSingleError(text, DatePatternErrorKind.TooLong, DatePattern.MaxLength);
    }

    [Fact]
    public void Pattern_of_exactly_200_characters_is_valid()
    {
        var text = "{YYYY}{MM}{DD}" + new string('x', DatePattern.MaxLength - 14);

        Assert.True(DatePattern.Parse(text).IsValid);
    }

    [Theory]
    [InlineData("IMG-{YYYY{MM}{DD}", DatePatternErrorKind.UnknownToken, 4, "YYYY{MM")]
    [InlineData("IMG-{YYYY}{MM}{DD", DatePatternErrorKind.UnclosedBrace, 14, null)]
    [InlineData("IMG-{YYYY}{MM}}{DD}", DatePatternErrorKind.UnexpectedClosingBrace, 14, null)]
    [InlineData("IMG-{YYYY}{MM}{DD}-{xyz}", DatePatternErrorKind.UnknownToken, 19, "xyz")]
    [InlineData("IMG-{yyyy}{MM}{DD}", DatePatternErrorKind.UnknownToken, 4, "yyyy")]
    [InlineData("IMG-{}{YYYY}{MM}{DD}", DatePatternErrorKind.UnknownToken, 4, "")]
    [InlineData("{DD} {MMM} {YYYY}", DatePatternErrorKind.UnsupportedToken, 5, "MMM")]
    [InlineData("{YYYY}{MM}{DD} {name}", DatePatternErrorKind.UnsupportedToken, 15, "name")]
    [InlineData("{YYYY}{MM}{DD} {camera}", DatePatternErrorKind.UnsupportedToken, 15, "camera")]
    [InlineData("{YYYY}{MM}{DD} {counter}", DatePatternErrorKind.UnsupportedToken, 15, "counter")]
    [InlineData("{dddd} {YYYY}{MM}{DD}", DatePatternErrorKind.UnsupportedToken, 0, "dddd")]
    [InlineData("{YYYY}-{MM}-{DD}-{YYYY}", DatePatternErrorKind.DuplicateToken, 17, "YYYY")]
    [InlineData("{YYYY}-{MM}-{DD}-{YY}", DatePatternErrorKind.DuplicateToken, 17, "YY")]
    [InlineData("{YYYY}-{MM}-{DD}-{M}", DatePatternErrorKind.DuplicateToken, 17, "M")]
    [InlineData("IMG-{MM}{DD}-WA*", DatePatternErrorKind.MissingYear, 0, null)]
    [InlineData("IMG-{YYYY}{DD}-WA*", DatePatternErrorKind.MissingMonth, 0, null)]
    [InlineData("IMG-{YYYY}{MM}-WA*", DatePatternErrorKind.MissingDay, 0, null)]
    [InlineData("{YYYY}{MM}{DD}_{HH}", DatePatternErrorKind.HourWithoutMinute, 15, "HH")]
    [InlineData("{YYYY}{MM}{DD}_{mm}", DatePatternErrorKind.MinuteWithoutHour, 15, "mm")]
    [InlineData("{YYYY}{MM}{DD}_{HH}{ss}", DatePatternErrorKind.HourWithoutMinute, 15, "HH")]
    [InlineData("{YYYY}{MM}{DD}_{HH}-{mm}-{s}x{s}", DatePatternErrorKind.DuplicateToken, 29, "s")]
    [InlineData("IMG-*{M}-{YYYY}{DD}", DatePatternErrorKind.AmbiguousNeighbours, 4, "M")]
    [InlineData("IMG-{YYYY}{MM}{D}*", DatePatternErrorKind.AmbiguousNeighbours, 17, "D")]
    [InlineData("IMG-{YYYY}{MM}{D}?", DatePatternErrorKind.AmbiguousNeighbours, 17, "D")]
    [InlineData("IMG-{YYYY}?{M}-{DD}", DatePatternErrorKind.AmbiguousNeighbours, 10, "M")]
    [InlineData("IMG-{YYYY}-{M}{D}", DatePatternErrorKind.AmbiguousNeighbours, 14, "D")]
    [InlineData("IMG-{YYYY}-{MM}-{DD} {H}{m}", DatePatternErrorKind.AmbiguousNeighbours, 24, "m")]
    public void Invalid_patterns_report_kind_and_position(string text, DatePatternErrorKind kind, int position, string? detail)
    {
        var result = DatePattern.Parse(text);

        Assert.False(result.IsValid);
        Assert.Null(result.Pattern);
        Assert.Contains(new DatePatternError(kind, position, detail), result.Errors);
    }

    [Fact]
    public void Second_without_minute_is_an_error()
    {
        AssertSingleError("{YYYY}{MM}{DD}_{ss}", DatePatternErrorKind.SecondWithoutMinute, 15, "ss");
    }

    [Fact]
    public void Every_error_kind_is_covered()
    {
        string?[] samples =
        [
            "",
            new string('x', DatePattern.MaxLength + 1),
            "{YYYY",
            "{YYYY}}{MM}{DD}",
            "{foo}",
            "{MMM}",
            "{YYYY}{YYYY}",
            "{MM}{DD}",
            "{YYYY}{DD}",
            "{YYYY}{MM}",
            "{YYYY}{MM}{DD}{HH}",
            "{YYYY}{MM}{DD}{mm}",
            "{YYYY}{MM}{DD}{ss}",
            "{YYYY}{MM}{DD}*{H}{mm}",
        ];

        var seen = samples.SelectMany(sample => DatePattern.Parse(sample).Errors).Select(error => error.Kind).ToHashSet();

        Assert.Equal(Enum.GetValues<DatePatternErrorKind>().ToHashSet(), seen);
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        var result = DatePattern.Parse("{foo}-{MMM}");

        Assert.Contains(result.Errors, error => error.Kind == DatePatternErrorKind.UnknownToken);
        Assert.Contains(result.Errors, error => error.Kind == DatePatternErrorKind.UnsupportedToken);
    }

    [Theory]
    [InlineData("{YYYY}{MM}{DD}*")]
    [InlineData("*{YYYY}{MM}{DD}")]
    [InlineData("WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*")]
    [InlineData("{YYYY}-{M}-{D} *")]
    public void Wildcards_next_to_fixed_width_tokens_are_allowed(string text)
    {
        Assert.True(DatePattern.Parse(text).IsValid);
    }

    private static void AssertSingleError(string? text, DatePatternErrorKind kind, int position, string? detail = null)
    {
        var result = DatePattern.Parse(text);

        Assert.False(result.IsValid);
        Assert.Null(result.Pattern);
        var error = Assert.Single(result.Errors);
        Assert.Equal(new DatePatternError(kind, position, detail), error);
    }
}
