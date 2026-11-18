using System.Diagnostics;
using System.Text.RegularExpressions;
using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

/// <summary><see cref="DateExpression.TryMatch"/> and <see cref="DateExpression.Check"/>.</summary>
public sealed class DateExpressionMatchTests
{
    private const string WhatsAppAndroid = @"^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$";

    private static readonly DateTime Now = new(2026, 11, 18, 8, 6, 0);

    [Theory]
    [InlineData("IMG-20260812-WA0017", 2026, 8, 12)]
    [InlineData("img-20160706-wa0002", 2016, 7, 6)]
    public void The_help_example_reads_whatsapp_names(string name, int year, int month, int day)
    {
        Assert.True(Expression(WhatsAppAndroid).TryMatch(name, Now, out var date));
        Assert.Equal(new FileNameDate(new DateOnly(year, month, day), null), date);
    }

    [Fact]
    public void Date_and_time_are_read()
    {
        var expression = Expression(@"WhatsApp .* (?<year>\d{4})-(?<month>\d\d)-(?<day>\d\d) .* (?<hour>\d\d)\.(?<minute>\d\d)\.(?<second>\d\d).*");

        Assert.True(expression.TryMatch("WhatsApp Image 2024-07-14 at 15.32.08 (1)", Now, out var date));
        Assert.Equal(new FileNameDate(new DateOnly(2024, 7, 14), new TimeOnly(15, 32, 8)), date);
    }

    [Fact]
    public void A_two_digit_year_uses_the_same_pivot_as_patterns()
    {
        var expression = Expression(@"(?<day>\d\d)\.(?<month>\d\d)\.(?<yy>\d\d)");

        Assert.True(expression.TryMatch("14.07.24", Now, out var recent));
        Assert.True(expression.TryMatch("14.07.98", Now, out var old));
        Assert.Equal(new DateOnly(2024, 7, 14), recent.Date);
        Assert.Equal(new DateOnly(1998, 7, 14), old.Date);
    }

    [Fact]
    public void Alternatives_may_use_the_same_group_names()
    {
        var expression = Expression(@"(?<year>\d{4})-(?<month>\d\d)-(?<day>\d\d)|(?<day>\d\d)\.(?<month>\d\d)\.(?<year>\d{4})");

        Assert.True(expression.TryMatch("2024-07-14", Now, out var iso));
        Assert.True(expression.TryMatch("14.07.2024", Now, out var german));
        Assert.Equal(iso, german);
    }

    [Theory]
    [InlineData("xIMG-20240714-WA0001")]
    [InlineData("IMG-20240714-WA0001x")]
    [InlineData("IMG-20240714")]
    public void The_whole_name_must_match_even_without_anchors(string name)
    {
        var expression = Expression(@"IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+");

        Assert.False(expression.TryMatch(name, Now, out _));
        Assert.Equal(PatternCheckKind.NoMatch, expression.Check(name, Now).Kind);
    }

    [Theory]
    [InlineData("IMG-٢٠٢٤٠٧١٤-WA0001", "٢٠٢٤-٠٧-١٤")]
    [InlineData("IMG-２０２４０７１４-WA0001", "２０２４-０７-１４")]
    public void Non_ascii_digits_never_give_a_date(string name, string found)
    {
        var expression = Expression(WhatsAppAndroid);

        Assert.False(expression.TryMatch(name, Now, out _));
        var check = expression.Check(name, Now);
        Assert.Equal(PatternCheckKind.InvalidDate, check.Kind);
        Assert.Equal(found, check.Found);
    }

    [Theory]
    [InlineData("IMG-20241345-WA0001", PatternCheckKind.InvalidDate, "2024-13-45")]
    [InlineData("IMG-20240230-WA0001", PatternCheckKind.InvalidDate, "2024-02-30")]
    [InlineData("IMG-20991231-WA0001", PatternCheckKind.ImplausibleDate, "2099-12-31")]
    [InlineData("IMG-18991231-WA0001", PatternCheckKind.ImplausibleDate, "1899-12-31")]
    [InlineData("IMG-20240714-WA0001", PatternCheckKind.Date, "2024-07-14")]
    public void Calendar_and_plausibility_checks_apply(string name, PatternCheckKind kind, string found)
    {
        var check = Expression(WhatsAppAndroid).Check(name, Now);

        Assert.Equal(kind, check.Kind);
        Assert.Equal(found, check.Found);
    }

    [Theory]
    [InlineData("IMG-2024-07-14", @"IMG-(?<year>\d*)-(?<month>\d*)-(?<day>\d*)", PatternCheckKind.Date)]
    [InlineData("IMG--07-14", @"IMG-(?<year>\d*)-(?<month>\d*)-(?<day>\d*)", PatternCheckKind.InvalidDate)]
    [InlineData("IMG-020240-07-14", @"IMG-(?<year>\d*)-(?<month>\d*)-(?<day>\d*)", PatternCheckKind.InvalidDate)]
    [InlineData("IMG-abcd-07-14", @"IMG-(?<year>.*)-(?<month>\d*)-(?<day>\d*)", PatternCheckKind.InvalidDate)]
    [InlineData("IMG- 2024-07-14", @"IMG-(?<year>.*)-(?<month>\d*)-(?<day>\d*)", PatternCheckKind.InvalidDate)]
    [InlineData("IMG-+2024-07-14", @"IMG-(?<year>.*)-(?<month>\d*)-(?<day>\d*)", PatternCheckKind.InvalidDate)]
    public void Group_values_must_be_one_to_four_ascii_digits(string name, string text, PatternCheckKind kind)
    {
        Assert.Equal(kind, Expression(text).Check(name, Now).Kind);
    }

    [Fact]
    public void Long_captures_are_shortened_in_the_tester()
    {
        var check = Expression(@"(?<year>.*)-(?<month>\d\d)-(?<day>\d\d)").Check(new string('9', 200) + "-07-14", Now);

        Assert.Equal(PatternCheckKind.InvalidDate, check.Kind);
        Assert.Equal("9999…-07-14", check.Found);
    }

    [Fact]
    public void A_name_of_10000_characters_is_rejected_quickly()
    {
        var expression = Expression(@".*(?<year>\d{4})(?<month>\d{2})(?<day>\d{2}).*");
        var name = new string('1', 10_000);
        var watch = Stopwatch.StartNew();

        Assert.False(expression.TryMatch(name, Now, out _));
        Assert.Equal(PatternCheckKind.NoMatch, expression.Check(name, Now).Kind);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), watch.Elapsed.ToString());
    }

    [Fact]
    public void Names_up_to_260_characters_are_matched()
    {
        var expression = Expression(@".*(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})");

        Assert.True(expression.TryMatch(new string('a', DatePattern.MaxNameLength - 8) + "20240714", Now, out _));
        Assert.False(expression.TryMatch(new string('a', DatePattern.MaxNameLength - 7) + "20240714", Now, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_names_do_not_match(string? name)
    {
        Assert.False(Expression(WhatsAppAndroid).TryMatch(name, Now, out _));
        Assert.Equal(PatternCheckKind.NoMatch, Expression(WhatsAppAndroid).Check(name, Now).Kind);
    }

    [Fact]
    public void A_timeout_counts_as_no_match()
    {
        // The real expressions run with NonBacktracking and cannot be made slow enough on a 260-character name, so a
        // classic catastrophic expression stands in to prove the timeout path.
        var slow = new Regex("^(?<year>(a+)+)$", RegexOptions.None, TimeSpan.FromMilliseconds(1));
        var expression = new DateExpression("slow", slow);
        var name = new string('a', 40) + "!";
        var watch = Stopwatch.StartNew();

        Assert.False(expression.TryMatch(name, Now, out _));
        Assert.Equal(PatternCheckKind.NoMatch, expression.Check(name, Now).Kind);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
    }

    [Fact]
    public void Expressions_run_with_the_safe_options_and_the_timeout()
    {
        var regex = Expression(WhatsAppAndroid).Regex;

        Assert.True(regex.Options.HasFlag(RegexOptions.NonBacktracking));
        Assert.True(regex.Options.HasFlag(RegexOptions.IgnoreCase));
        Assert.True(regex.Options.HasFlag(RegexOptions.CultureInvariant));
        Assert.True(regex.Options.HasFlag(RegexOptions.ExplicitCapture));
        Assert.Equal(TimeSpan.FromMilliseconds(50), regex.MatchTimeout);
        Assert.StartsWith("^(?:", regex.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(")$", regex.ToString(), StringComparison.Ordinal);
    }

    private static DateExpression Expression(string text) =>
        DateExpression.Parse(text).Expression ?? throw new InvalidOperationException(string.Join(", ", DateExpression.Parse(text).Errors));
}
