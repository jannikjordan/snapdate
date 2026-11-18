using Snapdate.Application.FileNames;
using Snapdate.Domain.FileNames;

namespace Snapdate.Application.Tests.FileNames;

public sealed class DateRuleCatalogTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 8, 6, 0);

    private static readonly DateRule Android = new(
        "whatsapp-android", "WhatsApp (Android)", RuleOrigin.System, Enabled: true,
        ["IMG-{YYYY}{MM}{DD}-WA*", "VID-{YYYY}{MM}{DD}-WA*"]);

    private static readonly DateRule Desktop = new(
        "whatsapp-desktop", "WhatsApp Desktop / Web", RuleOrigin.System, Enabled: true,
        ["WhatsApp * {YYYY}-{MM}-{DD} * {HH}.{mm}.{ss}*"]);

    private static readonly DateRule Scanner = new(
        "custom-3f9a1c2e", "Scanner", RuleOrigin.Custom, Enabled: true, ["Scan_{YYYY}-{MM}-{DD}*"]);

    private static DateRuleCatalog Catalog(UserDateRules? user = null, UserRulesStatus status = UserRulesStatus.Ok) =>
        new([Android, Desktop], new UserDateRulesLoad(user ?? UserDateRules.Empty, status));

    private static UserDateRules User(IReadOnlyList<string>? disabled = null, IReadOnlyList<DateRule>? custom = null) =>
        new(disabled ?? [], custom ?? []);

    [Fact]
    public void Without_user_rules_all_system_rules_are_enabled_in_shipped_order()
    {
        var catalog = Catalog(status: UserRulesStatus.Missing);

        Assert.Equal(["whatsapp-android", "whatsapp-desktop"], catalog.Rules.Select(r => r.Id));
        Assert.All(catalog.Rules, r => Assert.True(r.Enabled));
        Assert.Empty(catalog.CustomRules);
        Assert.False(catalog.IsReadOnly);
        Assert.Equal(UserRulesStatus.Missing, catalog.Status);
    }

    [Fact]
    public void Custom_rules_come_before_system_rules()
    {
        var other = Scanner with { Id = "custom-00000001", Name = "Other" };
        var catalog = Catalog(User(custom: [Scanner, other]));

        Assert.Equal(["custom-3f9a1c2e", "custom-00000001", "whatsapp-android", "whatsapp-desktop"], catalog.Rules.Select(r => r.Id));
        Assert.Equal(catalog.Rules, catalog.ToRuleSet().Rules);
    }

    [Fact]
    public void A_custom_rule_wins_over_a_system_rule_for_the_same_name()
    {
        var custom = new DateRule("custom-0a1b2c3d", "Mine", RuleOrigin.Custom, true, ["IMG-{YYYY}{DD}{MM}-WA*"]);
        var match = Catalog(User(custom: [custom])).ToRuleSet().Match("IMG-20241207-WA0001", Now);

        Assert.Equal("custom-0a1b2c3d", match?.RuleId);
        Assert.Equal(new DateOnly(2024, 7, 12), match?.Date.Date);
    }

    [Fact]
    public void Disabled_system_rules_are_marked_and_do_not_match()
    {
        var catalog = Catalog(User(disabled: ["whatsapp-android"]));

        Assert.False(catalog.Find("whatsapp-android")!.Enabled);
        Assert.True(catalog.Find("whatsapp-desktop")!.Enabled);
        Assert.Null(catalog.ToRuleSet().Match("IMG-20240714-WA0001", Now));
    }

    [Fact]
    public void Disabling_and_enabling_a_system_rule_updates_the_user_rules()
    {
        var catalog = Catalog();

        Assert.True(catalog.SetEnabled("whatsapp-desktop", false));
        Assert.Equal(["whatsapp-desktop"], catalog.ToUserRules().DisabledSystemRuleIds);
        Assert.False(catalog.Find("whatsapp-desktop")!.Enabled);

        Assert.True(catalog.SetEnabled("whatsapp-desktop", true));
        Assert.Empty(catalog.ToUserRules().DisabledSystemRuleIds);
        Assert.True(catalog.Find("whatsapp-desktop")!.Enabled);
    }

    [Fact]
    public void Disabling_twice_lists_the_id_once()
    {
        var catalog = Catalog();

        catalog.SetEnabled("whatsapp-android", false);
        catalog.SetEnabled("whatsapp-android", false);

        Assert.Equal(["whatsapp-android"], catalog.ToUserRules().DisabledSystemRuleIds);
    }

    [Fact]
    public void Unknown_disabled_ids_are_preserved()
    {
        var catalog = Catalog(User(disabled: ["signal-android", "whatsapp-android"]));

        catalog.SetEnabled("whatsapp-android", true);
        catalog.SetEnabled("whatsapp-desktop", false);

        Assert.Equal(["signal-android", "whatsapp-desktop"], catalog.ToUserRules().DisabledSystemRuleIds);
    }

    [Fact]
    public void Enabling_a_custom_rule_changes_the_rule_itself()
    {
        var catalog = Catalog(User(custom: [Scanner]));

        Assert.True(catalog.SetEnabled(Scanner.Id, false));

        Assert.False(Assert.Single(catalog.ToUserRules().CustomRules).Enabled);
        Assert.Empty(catalog.ToUserRules().DisabledSystemRuleIds);
    }

    [Fact]
    public void Unknown_ids_are_rejected()
    {
        var catalog = Catalog();

        Assert.False(catalog.SetEnabled("nope", false));
        Assert.Null(catalog.Duplicate("nope"));
        Assert.False(catalog.Delete("nope"));
        Assert.Null(catalog.Find("nope"));
    }

    [Fact]
    public void Duplicate_creates_an_enabled_custom_copy_and_keeps_the_original()
    {
        var catalog = Catalog(User(disabled: ["whatsapp-android"]));

        var copy = catalog.Duplicate("whatsapp-android");

        Assert.NotNull(copy);
        Assert.Matches("^custom-[0-9a-f]{8}$", copy.Id);
        Assert.Equal("WhatsApp (Android) (copy)", copy.Name);
        Assert.Equal(RuleOrigin.Custom, copy.Origin);
        Assert.True(copy.Enabled);
        Assert.Equal(Android.Patterns, copy.Patterns);
        Assert.Equal(DateRuleSyntax.Pattern, copy.Syntax);
        Assert.Equal([copy.Id, "whatsapp-android", "whatsapp-desktop"], catalog.Rules.Select(r => r.Id));
        Assert.False(catalog.Find("whatsapp-android")!.Enabled);
        Assert.Equal("whatsapp-android", Assert.Single(catalog.ToUserRules().DisabledSystemRuleIds));
    }

    [Fact]
    public void Duplicate_accepts_a_caller_supplied_name()
    {
        var copy = Catalog().Duplicate("whatsapp-desktop", "WhatsApp Desktop / Web (Kopie)");

        Assert.Equal("WhatsApp Desktop / Web (Kopie)", copy?.Name);
    }

    [Fact]
    public void Duplicate_keeps_the_copy_name_within_the_limit()
    {
        var longName = new string('a', UserDateRules.MaxNameLength);
        var catalog = Catalog(User(custom: [Scanner with { Name = longName }]));

        var copy = catalog.Duplicate(Scanner.Id);

        Assert.NotNull(copy);
        Assert.Equal(UserDateRules.MaxNameLength, copy.Name.Length);
        Assert.EndsWith(" (copy)", copy.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicates_get_distinct_ids()
    {
        var catalog = Catalog();

        var ids = Enumerable.Range(0, 20).Select(_ => catalog.Duplicate("whatsapp-android")!.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Add_appends_a_custom_rule()
    {
        var catalog = Catalog(User(custom: [Scanner]));

        var added = catalog.Add("Camera", ["DSC_{YYYY}{MM}{DD}*"]);

        Assert.NotNull(added);
        Assert.Matches("^custom-[0-9a-f]{8}$", added.Id);
        Assert.Equal([Scanner.Id, added.Id], catalog.CustomRules.Select(r => r.Id));
        Assert.Equal(new DateOnly(2024, 7, 14), catalog.ToRuleSet().Match("DSC_20240714_1", Now)?.Date.Date);
    }

    [Fact]
    public void Add_keeps_rules_in_another_syntax()
    {
        var added = Catalog().Add("Regex", ["(?<year>[0-9]{4})"], "regex");

        Assert.Equal("regex", added?.Syntax);
        Assert.False(added?.IsSyntaxSupported);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_rejects_an_empty_name(string name)
    {
        Assert.Null(Catalog().Add(name, ["{YYYY}{MM}{DD}"]));
    }

    [Fact]
    public void Add_rejects_invalid_shapes()
    {
        var catalog = Catalog();

        Assert.Null(catalog.Add(new string('a', UserDateRules.MaxNameLength + 1), ["{YYYY}{MM}{DD}"]));
        Assert.Null(catalog.Add("No patterns", []));
        Assert.Null(catalog.Add("Too many", [.. Enumerable.Repeat("{YYYY}{MM}{DD}", DateRule.MaxPatterns + 1)]));
        Assert.Empty(catalog.CustomRules);
    }

    [Fact]
    public void Add_stops_at_the_custom_rule_limit()
    {
        var catalog = Catalog();
        for (var i = 0; i < UserDateRules.MaxCustomRules; i++)
        {
            Assert.NotNull(catalog.Add($"Rule {i}", ["{YYYY}{MM}{DD}"]));
        }

        Assert.Null(catalog.Add("One too many", ["{YYYY}{MM}{DD}"]));
        Assert.Null(catalog.Duplicate("whatsapp-android"));
        Assert.Equal(UserDateRules.MaxCustomRules, catalog.CustomRules.Count);
    }

    [Fact]
    public void Update_replaces_a_custom_rule_in_place()
    {
        var other = Scanner with { Id = "custom-00000001", Name = "Other" };
        var catalog = Catalog(User(custom: [Scanner, other]));

        var changed = Scanner with { Name = "Scanner 2", Patterns = ["Scan-{YYYY}{MM}{DD}*"], Enabled = false };
        Assert.True(catalog.Update(changed));

        Assert.Equal([changed, other], catalog.CustomRules);
    }

    [Fact]
    public void Update_rejects_system_rules_unknown_ids_and_invalid_rules()
    {
        var catalog = Catalog(User(custom: [Scanner]));

        Assert.False(catalog.Update(Android with { Name = "Hacked" }));
        Assert.False(catalog.Update(Scanner with { Id = "custom-ffffffff" }));
        Assert.False(catalog.Update(Scanner with { Patterns = [] }));
        Assert.False(catalog.Update(Scanner with { Name = "" }));
        Assert.Equal("WhatsApp (Android)", catalog.Find("whatsapp-android")!.Name);
        Assert.Equal([Scanner], catalog.CustomRules);
    }

    [Fact]
    public void Delete_removes_custom_rules_but_never_system_rules()
    {
        var catalog = Catalog(User(custom: [Scanner]));

        Assert.False(catalog.Delete("whatsapp-android"));
        Assert.True(catalog.Delete(Scanner.Id));

        Assert.Empty(catalog.CustomRules);
        Assert.Equal(2, catalog.SystemRules.Count);
    }

    [Fact]
    public void Newer_version_is_used_but_read_only()
    {
        var catalog = Catalog(User(disabled: ["whatsapp-android"], custom: [Scanner]), UserRulesStatus.NewerVersion);

        Assert.True(catalog.IsReadOnly);
        Assert.Equal([Scanner.Id, "whatsapp-android", "whatsapp-desktop"], catalog.Rules.Select(r => r.Id));
        Assert.False(catalog.Find("whatsapp-android")!.Enabled);

        Assert.False(catalog.SetEnabled("whatsapp-android", true));
        Assert.Null(catalog.Duplicate("whatsapp-desktop"));
        Assert.Null(catalog.Add("New", ["{YYYY}{MM}{DD}"]));
        Assert.False(catalog.Update(Scanner with { Name = "Changed" }));
        Assert.False(catalog.Delete(Scanner.Id));
        Assert.Equal([Scanner], catalog.CustomRules);
    }

    [Fact]
    public void Unsupported_syntax_rules_are_kept_but_inactive()
    {
        var regex = new DateRule("custom-0000abcd", "Regex", RuleOrigin.Custom, true, ["(?<year>[0-9]{4})"], "regex");
        var catalog = Catalog(User(custom: [regex]));

        Assert.Equal(regex, catalog.Find(regex.Id));
        Assert.Equal([regex], catalog.ToUserRules().CustomRules);
        Assert.Equal("whatsapp-android", catalog.ToRuleSet().Match("IMG-20240714-WA0001", Now)?.RuleId);
    }

    [Fact]
    public void Custom_rules_that_reuse_a_system_id_or_repeat_an_id_are_dropped()
    {
        var clash = Scanner with { Id = "whatsapp-android", Name = "Clash" };
        var repeat = Scanner with { Name = "Repeat" };
        var catalog = Catalog(User(custom: [clash, Scanner, repeat]));

        Assert.Equal([Scanner], catalog.CustomRules);
        Assert.Equal(RuleOrigin.System, catalog.Find("whatsapp-android")!.Origin);
    }

    [Fact]
    public void Custom_rules_are_always_reported_as_custom()
    {
        var mislabelled = Scanner with { Origin = RuleOrigin.System };
        var catalog = Catalog(User(custom: [mislabelled]));

        Assert.Equal(RuleOrigin.Custom, Assert.Single(catalog.CustomRules).Origin);
    }

    [Fact]
    public void User_rules_round_trip_unchanged_through_the_catalog()
    {
        var user = User(disabled: ["whatsapp-desktop", "gone-rule"], custom: [Scanner]);

        Assert.Equal(user, Catalog(user).ToUserRules());
    }

    [Theory]
    [InlineData("whatsapp-android", true)]
    [InlineData("custom-3f9a1c2e", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("WhatsApp", false)]
    [InlineData("custom_1", false)]
    [InlineData("rule id", false)]
    [InlineData("ü", false)]
    public void Ids_follow_the_documented_shape(string? id, bool valid)
    {
        Assert.Equal(valid, UserDateRules.IsValidId(id));
    }

    [Fact]
    public void Ids_longer_than_64_characters_are_invalid()
    {
        Assert.True(UserDateRules.IsValidId(new string('a', 64)));
        Assert.False(UserDateRules.IsValidId(new string('a', 65)));
    }
}
