using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

/// <summary><see cref="DatePattern.Check"/> (the editor's tester) and <see cref="DatePattern.SuggestFor"/>.</summary>
public sealed class DatePatternCheckTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 8, 6, 0);

    private static DatePattern Pattern(string text) => DatePattern.Parse(text).Pattern!;

    [Fact]
    public void A_valid_date_is_reported_with_the_numbers_found()
    {
        var check = Pattern("IMG-{YYYY}{MM}{DD}-WA*").Check("IMG-20240714-WA0001", Now);

        Assert.Equal(PatternCheckKind.Date, check.Kind);
        Assert.Equal(new FileNameDate(new DateOnly(2024, 7, 14), null), check.Date);
        Assert.Equal("2024-07-14", check.Found);
    }

    [Theory]
    [InlineData("IMG-20241345-WA0001", "2024-13-45")]
    [InlineData("IMG-20240230-WA0001", "2024-02-30")]
    [InlineData("IMG-00000101-WA0001", "0000-01-01")]
    public void Numbers_that_are_no_date_are_reported_as_invalid(string name, string found)
    {
        var check = Pattern("IMG-{YYYY}{MM}{DD}-WA*").Check(name, Now);

        Assert.Equal(PatternCheckKind.InvalidDate, check.Kind);
        Assert.Null(check.Date);
        Assert.Equal(found, check.Found);
    }

    [Theory]
    [InlineData("2024-07-14 2561", "2024-07-14 25:61")]
    [InlineData("2024-07-14 123460", "2024-07-14 12:34:60")]
    public void An_impossible_time_is_reported_as_invalid_with_the_time(string name, string found)
    {
        var pattern = name.Length > 15 ? "{YYYY}-{MM}-{DD} {HH}{mm}{ss}" : "{YYYY}-{MM}-{DD} {HH}{mm}";

        var check = Pattern(pattern).Check(name, Now);

        Assert.Equal(PatternCheckKind.InvalidDate, check.Kind);
        Assert.Equal(found, check.Found);
    }

    [Theory]
    [InlineData("IMG-18991231-WA0001", "1899-12-31")]
    [InlineData("IMG-20991231-WA0001", "2099-12-31")]
    public void Dates_before_1900_or_in_the_future_are_reported_as_implausible(string name, string found)
    {
        var check = Pattern("IMG-{YYYY}{MM}{DD}-WA*").Check(name, Now);

        Assert.Equal(PatternCheckKind.ImplausibleDate, check.Kind);
        Assert.NotNull(check.Date);
        Assert.Equal(found, check.Found);
    }

    [Fact]
    public void A_two_digit_year_is_shown_as_the_year_it_stands_for()
    {
        var check = Pattern("{YY}-{MM}-{DD}").Check("24-13-01", Now);

        Assert.Equal(PatternCheckKind.InvalidDate, check.Kind);
        Assert.Equal("2024-13-01", check.Found);
    }

    [Theory]
    [InlineData("IMG_4200")]
    [InlineData("")]
    [InlineData(null)]
    public void Names_that_do_not_fit_are_no_match(string? name)
    {
        var check = Pattern("IMG-{YYYY}{MM}{DD}-WA*").Check(name, Now);

        Assert.Same(PatternCheck.NoMatch, check);
        Assert.Equal(string.Empty, check.Found);
    }

    [Fact]
    public void Check_and_TryMatch_agree()
    {
        var pattern = Pattern("IMG-{YYYY}{MM}{DD}-WA*");
        foreach (var name in new[] { "IMG-20240714-WA0001", "IMG-20241345-WA0001", "IMG-18991231-WA0001", "x" })
        {
            Assert.Equal(pattern.TryMatch(name, Now, out _), pattern.Check(name, Now).Kind == PatternCheckKind.Date);
        }
    }

    // ---------- SuggestFor ----------

    [Theory]
    [InlineData("IMG_20240714_153208", "IMG_{YYYY}{MM}{DD}*")]
    [InlineData("PXL_20240714_153208123", "PXL_{YYYY}{MM}{DD}*")]
    [InlineData("Scan 20240714", "Scan {YYYY}{MM}{DD}")]
    [InlineData("20240714", "{YYYY}{MM}{DD}")]
    [InlineData("Party {1} 20240714-x", "Party {{1}} {YYYY}{MM}{DD}*")]
    [InlineData("IMG_4200 20240714 (1)", "IMG_4200 {YYYY}{MM}{DD}*")]
    public void A_name_with_an_eight_digit_date_gets_a_suggestion(string name, string expected)
    {
        var suggestion = DatePattern.SuggestFor(name);

        Assert.Equal(expected, suggestion);
        Assert.True(Pattern(suggestion!).TryMatch(name, Now, out var date));
        Assert.Equal(new DateOnly(2024, 7, 14), date.Date);
    }

    [Theory]
    [InlineData("IMG_4200")]
    [InlineData("20241345")]
    [InlineData("18991231")]
    [InlineData("202407141532")]
    [InlineData("2024-07-14")]
    [InlineData("")]
    [InlineData(null)]
    public void Names_without_a_clear_eight_digit_date_get_none(string? name) =>
        Assert.Null(DatePattern.SuggestFor(name));

    [Fact]
    public void The_first_date_like_run_is_used()
    {
        Assert.Equal("A_12345678_{YYYY}{MM}{DD}*", DatePattern.SuggestFor("A_12345678_20240714_1"));
    }
}
