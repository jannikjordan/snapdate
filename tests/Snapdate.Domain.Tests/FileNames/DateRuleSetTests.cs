using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

public sealed class DateRuleSetTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 8, 6, 0);

    private static readonly DateRule WhatsAppAndroid = new(
        "whatsapp-android",
        "WhatsApp (Android)",
        RuleOrigin.System,
        Enabled: true,
        ["IMG-{YYYY}{MM}{DD}-WA*", "VID-{YYYY}{MM}{DD}-WA*"]);

    private static readonly DateRule WhatsAppDesktop = new(
        "whatsapp-desktop",
        "WhatsApp Desktop / Web",
        RuleOrigin.System,
        Enabled: true,
        ["WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*"]);

    [Fact]
    public void Empty_set_never_matches()
    {
        Assert.Empty(DateRuleSet.Empty.Rules);
        Assert.Null(DateRuleSet.Empty.Match("IMG-20240714-WA0001", Now));
    }

    [Theory]
    [InlineData("IMG-20240714-WA0001", "whatsapp-android", 0, 2024, 7, 14)]
    [InlineData("VID-20240714-WA0001", "whatsapp-android", 1, 2024, 7, 14)]
    [InlineData("WhatsApp Image 2024-07-15 at 15.32.08", "whatsapp-desktop", 0, 2024, 7, 15)]
    public void Match_reports_rule_and_pattern_index(string name, string ruleId, int patternIndex, int year, int month, int day)
    {
        var set = new DateRuleSet([WhatsAppAndroid, WhatsAppDesktop]);

        var match = set.Match(name, Now);

        Assert.NotNull(match);
        Assert.Equal(ruleId, match.RuleId);
        Assert.Equal(patternIndex, match.PatternIndex);
        Assert.Equal(new DateOnly(year, month, day), match.Date.Date);
        Assert.Equal(set.Rules.Single(rule => rule.Id == ruleId).Name, match.RuleName);
    }

    [Theory]
    [InlineData("IMG_4200")]
    [InlineData("Screenshot 2024-07-15")]
    [InlineData("")]
    [InlineData(null)]
    public void Names_without_a_date_do_not_match(string? name)
    {
        Assert.Null(new DateRuleSet([WhatsAppAndroid, WhatsAppDesktop]).Match(name, Now));
    }

    [Fact]
    public void Custom_rule_listed_before_system_rule_wins()
    {
        var custom = new DateRule("custom-0a1b2c3d", "My phone", RuleOrigin.Custom, Enabled: true, ["IMG-{YYYY}{DD}{MM}-WA*"]);

        var match = new DateRuleSet([custom, WhatsAppAndroid]).Match("IMG-20241207-WA0001", Now);

        Assert.NotNull(match);
        Assert.Equal("custom-0a1b2c3d", match.RuleId);
        Assert.Equal(new DateOnly(2024, 7, 12), match.Date.Date);
    }

    [Fact]
    public void Next_rule_is_tried_when_the_first_gives_an_invalid_date()
    {
        var custom = new DateRule("custom-0a1b2c3d", "My phone", RuleOrigin.Custom, Enabled: true, ["IMG-{YYYY}{DD}{MM}-WA*"]);

        var match = new DateRuleSet([custom, WhatsAppAndroid]).Match("IMG-20240714-WA0001", Now);

        Assert.NotNull(match);
        Assert.Equal("whatsapp-android", match.RuleId);
        Assert.Equal(new DateOnly(2024, 7, 14), match.Date.Date);
    }

    [Fact]
    public void First_matching_pattern_within_a_rule_wins()
    {
        var rule = new DateRule("custom-1", "Two", RuleOrigin.Custom, Enabled: true, ["{YYYY}{MM}{DD}*", "{YYYY}{DD}{MM}*"]);

        var match = new DateRuleSet([rule]).Match("20240712", Now);

        Assert.NotNull(match);
        Assert.Equal(0, match.PatternIndex);
        Assert.Equal(new DateOnly(2024, 7, 12), match.Date.Date);
    }

    [Fact]
    public void Disabled_rules_are_skipped()
    {
        var disabled = WhatsAppAndroid with { Enabled = false };

        Assert.Null(new DateRuleSet([disabled]).Match("IMG-20240714-WA0001", Now));
    }

    [Fact]
    public void Invalid_patterns_are_skipped_but_valid_ones_in_the_same_rule_still_work()
    {
        var rule = new DateRule("custom-1", "Mixed", RuleOrigin.Custom, Enabled: true, ["{nope}", "", "IMG-{YYYY}{MM}{DD}-WA*"]);

        var match = new DateRuleSet([rule]).Match("IMG-20240714-WA0001", Now);

        Assert.NotNull(match);
        Assert.Equal(2, match.PatternIndex);
    }

    [Fact]
    public void Rule_with_only_invalid_patterns_never_matches()
    {
        var rule = new DateRule("custom-1", "Broken", RuleOrigin.Custom, Enabled: true, ["{YYYY", "{MM}{DD}"]);

        Assert.Null(new DateRuleSet([rule]).Match("IMG-20240714-WA0001", Now));
    }

    [Fact]
    public void Unknown_syntax_is_kept_but_never_matches()
    {
        var future = WhatsAppAndroid with { Syntax = "future-syntax" };

        var set = new DateRuleSet([future]);

        Assert.Same(future, Assert.Single(set.Rules));
        Assert.False(future.IsSyntaxSupported);
        Assert.Null(set.Match("IMG-20240714-WA0001", Now));
    }

    [Fact]
    public void Default_syntax_is_pattern()
    {
        Assert.Equal(DateRuleSyntax.Pattern, WhatsAppAndroid.Syntax);
        Assert.Equal("pattern", DateRuleSyntax.Pattern);
        Assert.True(WhatsAppAndroid.IsSyntaxSupported);
    }

    [Fact]
    public void Only_the_first_ten_patterns_of_a_rule_are_used()
    {
        var patterns = Enumerable.Range(0, DateRule.MaxPatterns).Select(i => $"P{i}-{{YYYY}}{{MM}}{{DD}}").Append("X-{YYYY}{MM}{DD}").ToArray();
        var rule = new DateRule("custom-1", "Many", RuleOrigin.Custom, Enabled: true, patterns);

        var set = new DateRuleSet([rule]);

        Assert.NotNull(set.Match("P9-20240714", Now));
        Assert.Null(set.Match("X-20240714", Now));
    }

    [Fact]
    public void Null_rules_and_patterns_are_ignored()
    {
        var rule = new DateRule("custom-1", "Nulls", RuleOrigin.Custom, Enabled: true, [null!, "IMG-{YYYY}{MM}{DD}-WA*"]);
        var noPatterns = new DateRule("custom-2", "None", RuleOrigin.Custom, Enabled: true, null!);

        var set = new DateRuleSet([null!, noPatterns, rule]);

        Assert.Equal(1, set.Match("IMG-20240714-WA0001", Now)?.PatternIndex);
    }

    [Fact]
    public void Rules_keep_the_given_order()
    {
        var set = new DateRuleSet([WhatsAppDesktop, WhatsAppAndroid]);

        Assert.Equal(["whatsapp-desktop", "whatsapp-android"], set.Rules.Select(rule => rule.Id));
    }

    [Fact]
    public void Rules_with_equal_patterns_are_equal()
    {
        var copy = WhatsAppAndroid with { Patterns = [.. WhatsAppAndroid.Patterns] };

        Assert.Equal(WhatsAppAndroid, copy);
        Assert.Equal(WhatsAppAndroid.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(WhatsAppAndroid, copy with { Patterns = ["IMG-{YYYY}{MM}{DD}-WA*"] });
    }

    [Fact]
    public void Too_long_names_do_not_match()
    {
        var name = "IMG-20240714-WA" + new string('0', 300);

        Assert.Null(new DateRuleSet([WhatsAppAndroid]).Match(name, Now));
    }
}
