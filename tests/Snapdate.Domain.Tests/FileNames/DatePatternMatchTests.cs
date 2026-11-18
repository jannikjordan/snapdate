using System.Diagnostics;
using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

public sealed class DatePatternMatchTests
{
    private const string WhatsAppAndroid = "IMG-{YYYY}{MM}{DD}-WA*";
    private const string WhatsAppDesktop = "WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*";

    private static readonly DateTime Now = new(2026, 11, 18, 8, 6, 0);

    [Theory]
    [InlineData("IMG-20260812-WA0017", 2026, 8, 12)]
    [InlineData("IMG-20261110-WA0009", 2026, 11, 10)]
    [InlineData("IMG-20260801-WA0030", 2026, 8, 1)]
    [InlineData("IMG-20261117-WA0000", 2026, 11, 17)]
    [InlineData("IMG-20260924-WA0003", 2026, 9, 24)]
    [InlineData("IMG-20160706-WA0002", 2016, 7, 6)]
    [InlineData("IMG-20261024-WA0028", 2026, 10, 24)]
    public void WhatsApp_android_samples_give_their_day_without_time(string name, int year, int month, int day)
    {
        var date = AssertMatch(WhatsAppAndroid, name);

        Assert.Equal(new FileNameDate(new DateOnly(year, month, day), null), date);
    }

    [Theory]
    [InlineData("WhatsApp Image 2024-07-14 at 15.32.08")]
    [InlineData("WhatsApp Bild 2024-07-14 um 15.32.08_1a2b3c4d")]
    [InlineData("WhatsApp Video 2024-07-14 at 15.32.08 (1)")]
    public void WhatsApp_desktop_names_give_date_and_time(string name)
    {
        var date = AssertMatch(WhatsAppDesktop, name);

        Assert.Equal(new FileNameDate(new DateOnly(2024, 7, 14), new TimeOnly(15, 32, 8)), date);
    }

    [Theory]
    [InlineData("IMG_4200")]
    [InlineData("IMG-20241332-WA0001")]
    [InlineData("IMG-20240230-WA0001")]
    [InlineData("IMG-20240700-WA0001")]
    [InlineData("IMG-20991231-WA0001")]
    [InlineData("IMG-18991231-WA0001")]
    [InlineData("IMG-٢٠٢٤٠٧١٤-WA0001")]
    [InlineData("IMG-２０２４０７１４-WA0001")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("IMG-20240714-W")]
    [InlineData("xIMG-20240714-WA0001")]
    [InlineData("IMG-2024071-WA0001")]
    public void Names_that_do_not_give_a_valid_date_do_not_match(string? name)
    {
        AssertNoMatch(WhatsAppAndroid, name);
    }

    [Fact]
    public void Names_longer_than_260_characters_do_not_match()
    {
        var name = "IMG-20240714-WA" + new string('0', 300 - 15);

        Assert.Equal(300, name.Length);
        AssertNoMatch(WhatsAppAndroid, name);
    }

    [Fact]
    public void Names_of_exactly_260_characters_still_match()
    {
        var name = "IMG-20240714-WA" + new string('0', DatePattern.MaxNameLength - 15);

        AssertMatch(WhatsAppAndroid, name);
    }

    [Theory]
    [InlineData("img-20240714-wa0001")]
    [InlineData("Img-20240714-Wa0001")]
    public void Matching_ignores_case(string name)
    {
        Assert.Equal(new DateOnly(2024, 7, 14), AssertMatch(WhatsAppAndroid, name).Date);
    }

    [Fact]
    public void Doubled_braces_match_literal_braces()
    {
        Assert.Equal(new DateOnly(2024, 7, 14), AssertMatch("{{{YYYY}-{MM}-{DD}}}", "{2024-07-14}").Date);
        AssertNoMatch("{{{YYYY}-{MM}-{DD}}}", "2024-07-14");
    }

    [Theory]
    [InlineData("a.b(c)[d]+e^f$g|h 2024-07-14", "a.b(c)[d]+e^f$g|h {YYYY}-{MM}-{DD}")]
    [InlineData("x\\d 2024-07-14", "x\\d {YYYY}-{MM}-{DD}")]
    public void Regex_characters_in_literals_are_plain_text(string name, string pattern)
    {
        Assert.Equal(new DateOnly(2024, 7, 14), AssertMatch(pattern, name).Date);
        AssertNoMatch(pattern, "ab(c)[d]+e^f$g|h 2024-07-14");
    }

    [Theory]
    [InlineData("{YYYY}{MM}{DD}?{HH}{mm}", "20240714_1532", 15, 32)]
    [InlineData("{YYYY}{MM}{DD}?{HH}{mm}", "20240714-0005", 0, 5)]
    public void Question_mark_matches_exactly_one_character(string pattern, string name, int hour, int minute)
    {
        Assert.Equal(new TimeOnly(hour, minute), AssertMatch(pattern, name).Time);
        AssertNoMatch(pattern, "20240714__1532");
        AssertNoMatch(pattern, "202407141532");
    }

    [Theory]
    [InlineData("{D}.{M}.{YYYY}", "4.7.2024", 2024, 7, 4)]
    [InlineData("{D}.{M}.{YYYY}", "14.12.2024", 2024, 12, 14)]
    [InlineData("{D}.{M}.{YYYY}", "04.07.2024", 2024, 7, 4)]
    public void Single_letter_tokens_take_one_or_two_digits(string pattern, string name, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), AssertMatch(pattern, name).Date);
        AssertNoMatch(pattern, "123.7.2024");
    }

    [Theory]
    [InlineData("{YYYY}-{MM}-{DD} {H}h{m}", "2024-07-14 9h5", 9, 5, 0)]
    [InlineData("{YYYY}-{MM}-{DD} {HH}{mm}", "2024-07-14 2359", 23, 59, 0)]
    [InlineData("{YYYY}-{MM}-{DD} {HH}{mm}{ss}", "2024-07-14 000000", 0, 0, 0)]
    [InlineData("{YYYY}-{MM}-{DD} {H}.{m}.{s}", "2024-07-14 7.8.9", 7, 8, 9)]
    public void Time_tokens_build_the_time(string pattern, string name, int hour, int minute, int second)
    {
        Assert.Equal(new TimeOnly(hour, minute, second), AssertMatch(pattern, name).Time);
    }

    [Theory]
    [InlineData("2024-07-14 2400")]
    [InlineData("2024-07-14 1260")]
    [InlineData("2024-07-14 123460")]
    public void Invalid_times_do_not_match(string name)
    {
        AssertNoMatch(name.Length > 15 ? "{YYYY}-{MM}-{DD} {HH}{mm}{ss}" : "{YYYY}-{MM}-{DD} {HH}{mm}", name);
    }

    [Theory]
    [InlineData("26-11-18", 2026)]
    [InlineData("26-11-20", 2026)]
    [InlineData("27-01-01", 1927)]
    [InlineData("99-12-31", 1999)]
    [InlineData("00-01-01", 2000)]
    public void Two_digit_years_are_20yy_unless_that_is_in_the_future(string name, int year)
    {
        Assert.Equal(year, AssertMatch("{YY}-{MM}-{DD}", name).Date.Year);
    }

    [Fact]
    public void Two_digit_year_in_the_current_year_but_beyond_the_tolerance_does_not_match()
    {
        AssertNoMatch("{YY}-{MM}-{DD}", "26-12-31");
    }

    [Fact]
    public void Dates_up_to_two_days_ahead_match_and_later_ones_do_not()
    {
        Assert.Equal(new TimeOnly(8, 6), AssertMatch("{YYYY}{MM}{DD}_{HH}{mm}", "20261120_0806").Time);
        AssertNoMatch("{YYYY}{MM}{DD}_{HH}{mm}", "20261120_0807");
    }

    [Theory]
    [InlineData("b")]
    [InlineData("2024071x")]
    public void Pathological_pattern_and_name_finish_quickly(string tail)
    {
        var pattern = DatePattern.Parse("*a*a*a*a*a*a*a*a*a*a*{YYYY}{MM}{DD}").Pattern!;
        var name = new string('a', DatePattern.MaxNameLength - tail.Length) + tail;

        // Warm up on a short input so the measurement covers matching, not JIT.
        pattern.TryMatch("a" + tail, Now, out _);

        var stopwatch = Stopwatch.StartNew();
        var matched = pattern.TryMatch(name, Now, out _);
        stopwatch.Stop();

        Assert.False(matched);
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void Failed_match_returns_default_date()
    {
        var pattern = DatePattern.Parse(WhatsAppAndroid).Pattern!;

        Assert.False(pattern.TryMatch("IMG_4200", Now, out var date));
        Assert.Equal(default, date);
    }

    private static FileNameDate AssertMatch(string patternText, string name)
    {
        var parsed = DatePattern.Parse(patternText);
        Assert.True(parsed.IsValid, string.Join(", ", parsed.Errors));
        Assert.True(parsed.Pattern!.TryMatch(name, Now, out var date), $"'{patternText}' should match '{name}'.");
        return date;
    }

    private static void AssertNoMatch(string patternText, string? name)
    {
        var parsed = DatePattern.Parse(patternText);
        Assert.True(parsed.IsValid, string.Join(", ", parsed.Errors));
        Assert.False(parsed.Pattern!.TryMatch(name, Now, out _), $"'{patternText}' should not match '{name}'.");
    }
}
