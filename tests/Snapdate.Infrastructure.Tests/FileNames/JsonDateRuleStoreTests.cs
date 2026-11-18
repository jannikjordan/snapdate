using System.Globalization;
using System.Text;
using Snapdate.Application.FileNames;
using Snapdate.Application.Ports;
using Snapdate.Domain.FileNames;
using Snapdate.Infrastructure.FileNames;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.Infrastructure.Tests.FileNames;

public sealed class JsonDateRuleStoreTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 18, 7, 6, 5, TimeSpan.Zero);

    private static readonly DateRule Scanner = new(
        "custom-3f9a1c2e", "Scanner", RuleOrigin.Custom, Enabled: true, ["Scan_{YYYY}-{MM}-{DD}*"]);

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string RulesPath => _temp.Combine("Snapdate", "date-rules.json");

    private JsonDateRuleStore Store() => new(RulesPath, new FixedClock(FixedNow));

    private string WriteRules(string json) => _temp.Write(Path.Combine("Snapdate", "date-rules.json"), Encoding.UTF8.GetBytes(json));

    // Saves based on the file as it is now, like a catalog loaded just before.
    private static DateRuleSaveResult SaveFresh(JsonDateRuleStore store, UserDateRules rules) =>
        store.SaveUserRules(rules, store.LoadUserRules().Fingerprint).Result;

    private static string UnreadableName() =>
        $"date-rules.unreadable-{FixedNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json";

    [Fact]
    public void Missing_file_is_reported_as_missing()
    {
        var load = Store().LoadUserRules();

        Assert.Equal(UserRulesStatus.Missing, load.Status);
        Assert.Equal(UserDateRules.Empty, load.Rules);
    }

    [Fact]
    public void User_rules_round_trip()
    {
        var regex = new DateRule("custom-0000abcd", "Regex", RuleOrigin.Custom, false, ["(?<year>[0-9]{4})"], "regex");
        var rules = new UserDateRules(["whatsapp-desktop", "gone-rule"], [Scanner, regex]);
        var store = Store();

        Assert.Equal(DateRuleSaveResult.Saved, SaveFresh(store, rules));
        var load = store.LoadUserRules();

        Assert.Equal(UserRulesStatus.Ok, load.Status);
        Assert.Equal(rules, load.Rules);
    }

    [Fact]
    public void Saved_file_is_compact_schema_v1_with_camel_case_names()
    {
        Assert.Equal(DateRuleSaveResult.Saved, SaveFresh(Store(), new UserDateRules(["whatsapp-desktop"], [Scanner])));

        var json = File.ReadAllText(RulesPath);
        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"disabledSystemRules\"", json, StringComparison.Ordinal);
        Assert.Contains("\"customRules\"", json, StringComparison.Ordinal);
        Assert.Contains("\"syntax\":\"pattern\"", json, StringComparison.Ordinal);
        Assert.Contains("\"enabled\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RulesPath)!, "*.tmp"));
    }

    [Fact]
    public void The_documented_example_file_loads()
    {
        WriteRules("""
            {
              "schemaVersion": 1,
              "disabledSystemRules": [ "whatsapp-desktop" ],
              "customRules": [
                { "id": "custom-3f9a1c2e", "name": "Scanner", "enabled": true,
                  "syntax": "pattern", "patterns": [ "Scan_{YYYY}-{MM}-{DD}*" ] }
              ]
            }
            """);

        var load = Store().LoadUserRules();

        Assert.Equal(UserRulesStatus.Ok, load.Status);
        Assert.Equal(new UserDateRules(["whatsapp-desktop"], [Scanner]), load.Rules);
    }

    [Fact]
    public void Missing_optional_fields_get_defaults()
    {
        WriteRules("""{ "schemaVersion": 1, "customRules": [ { "id": "custom-1", "name": "A", "patterns": [ "{YYYY}{MM}{DD}" ] } ] }""");

        var rule = Assert.Single(Store().LoadUserRules().Rules.CustomRules);

        Assert.True(rule.Enabled);
        Assert.Equal(DateRuleSyntax.Pattern, rule.Syntax);
        Assert.Equal(RuleOrigin.Custom, rule.Origin);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{ \"schemaVersion\": \"one\" }")]
    [InlineData("{ \"schemaVersion\": 0 }")]
    [InlineData("{ \"schemaVersion\": 1, \"customRules\": \"x\" }")]
    [InlineData("{ \"schemaVersion\": 1, \"disabledSystemRules\": [ 1 ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"customRules\": [ { \"id\": \"custom-1\", \"name\": 5, \"patterns\": [ \"x\" ] } ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"customRules\": [ { \"id\": \"custom-1\", \"name\": \"A\", \"enabled\": \"yes\", \"patterns\": [ \"x\" ] } ] }")]
    public void Corrupt_files_are_unreadable_and_do_not_affect_system_rules(string content)
    {
        WriteRules(content);
        var store = Store();

        var load = store.LoadUserRules();

        Assert.Equal(UserRulesStatus.Unreadable, load.Status);
        Assert.Equal(UserDateRules.Empty, load.Rules);
        Assert.Equal(2, store.LoadSystemRules().Count);
    }

    [Fact]
    public void Oversized_files_are_unreadable()
    {
        var padding = new string(' ', (int)JsonDateRuleStore.MaxFileSize);
        WriteRules($$"""{ "schemaVersion": 1 {{padding}} }""");

        Assert.Equal(UserRulesStatus.Unreadable, Store().LoadUserRules().Status);
    }

    [Fact]
    public void Saving_after_an_unreadable_file_keeps_the_bad_file_aside()
    {
        WriteRules("{ broken");
        var store = Store();

        Assert.Equal(DateRuleSaveResult.Saved, SaveFresh(store, new UserDateRules([], [Scanner])));

        var aside = Path.Combine(Path.GetDirectoryName(RulesPath)!, UnreadableName());
        Assert.Equal("{ broken", File.ReadAllText(aside));
        Assert.Equal(UserRulesStatus.Ok, store.LoadUserRules().Status);
    }

    [Fact]
    public void Moving_aside_never_overwrites_an_earlier_unreadable_file()
    {
        var folder = Path.GetDirectoryName(RulesPath)!;
        var store = Store();
        WriteRules("{ first");
        Assert.Equal(DateRuleSaveResult.Saved, SaveFresh(store, UserDateRules.Empty));
        WriteRules("{ second");
        Assert.Equal(DateRuleSaveResult.Saved, SaveFresh(store, UserDateRules.Empty));

        var aside = Directory.GetFiles(folder, "date-rules.unreadable-*.json").Select(File.ReadAllText).Order(StringComparer.Ordinal);
        Assert.Equal(["{ first", "{ second"], aside);
    }

    [Fact]
    public void Newer_schema_versions_are_used_but_never_saved()
    {
        var newer = """
            {
              "schemaVersion": 2,
              "somethingNew": { "x": 1 },
              "disabledSystemRules": [ "whatsapp-android" ],
              "customRules": [
                { "id": "custom-3f9a1c2e", "name": "Scanner", "enabled": true,
                  "syntax": "pattern", "patterns": [ "Scan_{YYYY}-{MM}-{DD}*" ] }
              ]
            }
            """;
        WriteRules(newer);
        var store = Store();

        var load = store.LoadUserRules();

        Assert.Equal(UserRulesStatus.NewerVersion, load.Status);
        Assert.Equal(new UserDateRules(["whatsapp-android"], [Scanner]), load.Rules);
        Assert.Equal(DateRuleSaveResult.ReadOnly, SaveFresh(store, UserDateRules.Empty));
        Assert.Equal(newer, File.ReadAllText(RulesPath));
    }

    [Fact]
    public void Newer_schema_versions_with_unknown_shapes_are_kept_untouched()
    {
        var newer = """{ "schemaVersion": 3, "customRules": { "byId": {} } }""";
        WriteRules(newer);
        var store = Store();

        var load = store.LoadUserRules();

        Assert.Equal(UserRulesStatus.NewerVersion, load.Status);
        Assert.Equal(UserDateRules.Empty, load.Rules);
        Assert.Equal(DateRuleSaveResult.ReadOnly, SaveFresh(store, UserDateRules.Empty));
        Assert.Equal(newer, File.ReadAllText(RulesPath));
    }

    [Fact]
    public void Invalid_entries_are_dropped_and_the_rest_is_kept()
    {
        var tooManyPatterns = string.Join(", ", Enumerable.Repeat("\"{YYYY}{MM}{DD}\"", DateRule.MaxPatterns + 1));
        var longName = new string('n', UserDateRules.MaxNameLength + 1);
        WriteRules($$"""
            {
              "schemaVersion": 1,
              "disabledSystemRules": [ "whatsapp-android", "Not Valid", "", "whatsapp-android" ],
              "customRules": [
                { "id": "custom-00000001", "name": "Good", "patterns": [ "{YYYY}{MM}{DD}" ] },
                { "id": "custom-00000002", "name": "Too many", "patterns": [ {{tooManyPatterns}} ] },
                { "id": "custom-00000003", "name": "{{longName}}", "patterns": [ "{YYYY}{MM}{DD}" ] },
                { "id": "Custom_4", "name": "Bad id", "patterns": [ "{YYYY}{MM}{DD}" ] },
                { "id": "custom-00000005", "name": "No patterns", "patterns": [] },
                { "id": "custom-00000006", "name": "Null pattern", "patterns": [ null ] },
                { "name": "No id", "patterns": [ "{YYYY}{MM}{DD}" ] },
                null,
                { "id": "custom-00000001", "name": "Repeated id", "patterns": [ "{YYYY}{MM}{DD}" ] },
                { "id": "custom-00000007", "name": "Also good", "patterns": [ "x{YYYY}{MM}{DD}" ] }
              ]
            }
            """);

        var load = Store().LoadUserRules();

        Assert.Equal(UserRulesStatus.Ok, load.Status);
        Assert.Equal(["whatsapp-android"], load.Rules.DisabledSystemRuleIds);
        Assert.Equal(["Good", "Also good"], load.Rules.CustomRules.Select(r => r.Name));
    }

    [Fact]
    public void Custom_rules_beyond_the_limit_are_dropped()
    {
        var entries = Enumerable.Range(0, UserDateRules.MaxCustomRules + 5)
            .Select(i => $$"""{ "id": "custom-{{i:x8}}", "name": "R{{i}}", "patterns": [ "{YYYY}{MM}{DD}" ] }""");
        WriteRules($$"""{ "schemaVersion": 1, "customRules": [ {{string.Join(",", entries)}} ] }""");

        var rules = Store().LoadUserRules().Rules.CustomRules;

        Assert.Equal(UserDateRules.MaxCustomRules, rules.Count);
        Assert.Equal("R0", rules[0].Name);
    }

    [Fact]
    public void Unknown_syntax_is_kept_and_written_back_unchanged()
    {
        WriteRules("""{ "schemaVersion": 1, "customRules": [ { "id": "custom-1", "name": "Future", "enabled": true, "syntax": "glob2", "patterns": [ "a" ] } ] }""");
        var store = Store();

        var rule = Assert.Single(store.LoadUserRules().Rules.CustomRules);
        Assert.Equal("glob2", rule.Syntax);
        Assert.False(rule.IsSyntaxSupported);

        Assert.Equal(DateRuleSaveResult.Saved, SaveFresh(store, store.LoadUserRules().Rules));
        Assert.Equal("glob2", Assert.Single(store.LoadUserRules().Rules.CustomRules).Syntax);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        WriteRules("""
            {
              // edited by hand
              "schemaVersion": 1,
              "disabledSystemRules": [ "whatsapp-desktop", ],
            }
            """);

        var load = Store().LoadUserRules();

        Assert.Equal(UserRulesStatus.Ok, load.Status);
        Assert.Equal(["whatsapp-desktop"], load.Rules.DisabledSystemRuleIds);
    }

    [Fact]
    public void Rules_too_large_for_the_file_are_not_saved()
    {
        var pattern = "{YYYY}{MM}{DD}" + new string('x', DatePattern.MaxLength - 14);
        var rules = Enumerable.Range(0, UserDateRules.MaxCustomRules)
            .Select(i => new DateRule($"custom-{i:x8}", $"Rule {i}", RuleOrigin.Custom, true, [.. Enumerable.Repeat(pattern, DateRule.MaxPatterns)]))
            .ToList();
        WriteRules("""{ "schemaVersion": 1 }""");
        var store = Store();

        Assert.Equal(DateRuleSaveResult.TooLarge, SaveFresh(store, new UserDateRules([], rules)));
        Assert.Equal("""{ "schemaVersion": 1 }""", File.ReadAllText(RulesPath));
    }

    [Fact]
    public void A_file_with_a_utf8_byte_order_mark_loads()
    {
        var json = """{ "schemaVersion": 1, "disabledSystemRules": [ "whatsapp-desktop" ] }""";
        _temp.Write(Path.Combine("Snapdate", "date-rules.json"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)]);

        var load = Store().LoadUserRules();

        Assert.Equal(UserRulesStatus.Ok, load.Status);
        Assert.Equal(["whatsapp-desktop"], load.Rules.DisabledSystemRuleIds);
    }

    [Fact]
    public void A_locked_file_is_inaccessible_and_never_overwritten()
    {
        var original = """{ "schemaVersion": 1, "customRules": [ { "id": "custom-3f9a1c2e", "name": "Scanner", "patterns": [ "Scan_{YYYY}-{MM}-{DD}*" ] } ] }""";
        WriteRules(original);
        var store = Store();

        UserDateRulesLoad load;
        using (new FileStream(RulesPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            load = store.LoadUserRules();
            Assert.Equal(DateRuleSaveResult.Failed, SaveFresh(store, UserDateRules.Empty));
        }

        Assert.Equal(UserRulesStatus.Inaccessible, load.Status);
        Assert.Equal(UserDateRules.Empty, load.Rules);
        Assert.Equal(original, File.ReadAllText(RulesPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RulesPath)!, "date-rules.unreadable-*"));

        // Once the lock is gone the earlier rules come back unchanged.
        Assert.Equal([Scanner], store.LoadUserRules().Rules.CustomRules);
    }

    [Fact]
    public void Save_is_aborted_when_the_unreadable_file_cannot_be_moved_aside()
    {
        WriteRules("{ broken");
        var store = Store();

        using (new FileStream(RulesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(UserRulesStatus.Unreadable, store.LoadUserRules().Status);
            Assert.Equal(DateRuleSaveResult.Failed, SaveFresh(store, new UserDateRules([], [Scanner])));
        }

        Assert.Equal("{ broken", File.ReadAllText(RulesPath));
        Assert.Equal([RulesPath], Directory.GetFiles(Path.GetDirectoryName(RulesPath)!));
    }
    [Fact]
    public void A_file_repaired_after_an_unreadable_load_is_not_overwritten()
    {
        WriteRules("{ broken");
        var store = Store();
        var load = store.LoadUserRules();
        Assert.Equal(UserRulesStatus.Unreadable, load.Status);

        var repaired = """{ "schemaVersion": 1, "disabledSystemRules": [ "whatsapp-desktop" ] }""";
        WriteRules(repaired);
        var saved = store.SaveUserRules(new UserDateRules([], [Scanner]), load.Fingerprint);

        Assert.Equal(DateRuleSaveResult.Conflict, saved.Result);
        Assert.Equal(load.Fingerprint, saved.Fingerprint);
        Assert.Equal(repaired, File.ReadAllText(RulesPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RulesPath)!, "date-rules.unreadable-*"));
    }

    [Fact]
    public void A_file_created_by_another_writer_after_a_missing_load_is_not_overwritten()
    {
        var store = Store();
        var load = store.LoadUserRules();
        Assert.Equal(UserRulesFingerprint.Absent, load.Fingerprint);

        var other = """{ "schemaVersion": 1, "disabledSystemRules": [ "whatsapp-android" ] }""";
        WriteRules(other);

        Assert.Equal(DateRuleSaveResult.Conflict, store.SaveUserRules(new UserDateRules([], [Scanner]), load.Fingerprint).Result);
        Assert.Equal(other, File.ReadAllText(RulesPath));
    }

    [Fact]
    public void A_file_changed_by_another_writer_after_loading_is_not_overwritten()
    {
        var store = Store();
        Assert.Equal(DateRuleSaveResult.Saved, SaveFresh(store, new UserDateRules([], [Scanner])));
        var load = store.LoadUserRules();
        Assert.Equal(UserRulesStatus.Ok, load.Status);

        var other = """{ "schemaVersion": 1, "disabledSystemRules": [ "whatsapp-android" ] }""";
        WriteRules(other);

        Assert.Equal(DateRuleSaveResult.Conflict, store.SaveUserRules(UserDateRules.Empty, load.Fingerprint).Result);
        Assert.Equal(other, File.ReadAllText(RulesPath));
    }

    [Fact]
    public void An_unknown_fingerprint_never_overwrites_or_creates_a_file()
    {
        var store = Store();

        Assert.Equal(DateRuleSaveResult.Conflict, store.SaveUserRules(UserDateRules.Empty, UserRulesFingerprint.Unknown).Result);
        Assert.False(File.Exists(RulesPath));
    }

    [Fact]
    public void Saving_returns_the_fingerprint_of_the_written_file()
    {
        var store = Store();

        var saved = store.SaveUserRules(new UserDateRules([], [Scanner]), store.LoadUserRules().Fingerprint);

        Assert.Equal(DateRuleSaveResult.Saved, saved.Result);
        Assert.Equal(store.LoadUserRules().Fingerprint, saved.Fingerprint);
        Assert.Equal(new FileInfo(RulesPath).Length, saved.Fingerprint.Length);
    }

    [Fact]
    public void A_catalog_saves_again_after_its_own_save()
    {
        WriteRules("{ broken");
        var store = Store();
        var catalog = DateRuleCatalog.Load(store);
        Assert.Equal(UserRulesStatus.Unreadable, catalog.Status);

        Assert.NotNull(catalog.Add("Camera", ["DSC_{YYYY}{MM}{DD}*"]));
        Assert.Equal(DateRuleSaveResult.Saved, catalog.Save(store));
        Assert.Equal(UserRulesStatus.Ok, catalog.Status);

        Assert.True(catalog.SetEnabled("whatsapp-desktop", false));
        Assert.Equal(DateRuleSaveResult.Saved, catalog.Save(store));

        Assert.Equal(catalog.ToUserRules(), store.LoadUserRules().Rules);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(RulesPath)!, "date-rules.unreadable-*"));
    }

    [Fact]
    public void A_second_catalog_cannot_overwrite_the_first_ones_save()
    {
        var store = Store();
        var first = DateRuleCatalog.Load(store);
        var second = DateRuleCatalog.Load(store);

        first.Add("First", ["A_{YYYY}{MM}{DD}"]);
        second.Add("Second", ["B_{YYYY}{MM}{DD}"]);

        Assert.Equal(DateRuleSaveResult.Saved, first.Save(store));
        Assert.Equal(DateRuleSaveResult.Conflict, second.Save(store));
        Assert.Equal(["First"], store.LoadUserRules().Rules.CustomRules.Select(r => r.Name));
    }
    [Fact]
    public void Default_path_is_next_to_the_settings()
    {
        Assert.EndsWith(Path.Combine("Snapdate", "date-rules.json"), JsonDateRuleStore.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
