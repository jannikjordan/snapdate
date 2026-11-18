using System.Text.Json;
using Snapdate.Application.FileNames;
using Snapdate.Domain.FileNames;
using Snapdate.Infrastructure.FileNames;

namespace Snapdate.Infrastructure.Tests.FileNames;

/// <summary>Guards the rules that ship inside the exe against a broken or misspelt rule.</summary>
public sealed class SystemDateRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 8, 6, 0);

    private static IReadOnlyList<DateRule> Load() => new JsonDateRuleStore(Path.Combine(Path.GetTempPath(), "unused.json")).LoadSystemRules();

    private static JsonElement RawRules()
    {
        using var stream = typeof(JsonDateRuleStore).Assembly.GetManifestResourceStream(JsonDateRuleStore.SystemRulesResourceName);
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        return document.RootElement.GetProperty("rules").Clone();
    }

    [Fact]
    public void Embedded_rules_load_in_shipped_order()
    {
        var rules = Load();

        Assert.Equal(["whatsapp-android", "whatsapp-desktop"], rules.Select(r => r.Id));
        Assert.Equal(["WhatsApp (Android)", "WhatsApp Desktop / Web"], rules.Select(r => r.Name));
        Assert.All(rules, rule =>
        {
            Assert.Equal(RuleOrigin.System, rule.Origin);
            Assert.True(rule.Enabled);
            Assert.Equal(DateRuleSyntax.Pattern, rule.Syntax);
        });
    }

    [Fact]
    public void No_embedded_rule_is_dropped_while_loading()
    {
        Assert.Equal(RawRules().GetArrayLength(), Load().Count);
    }

    [Fact]
    public void Every_embedded_pattern_parses()
    {
        Assert.All(Load().SelectMany(rule => rule.Patterns), pattern =>
            Assert.True(DatePattern.Parse(pattern).IsValid, $"Pattern '{pattern}' does not parse."));
    }

    [Fact]
    public void Embedded_ids_are_unique_and_valid()
    {
        var ids = RawRules().EnumerateArray().Select(rule => rule.GetProperty("id").GetString()).ToList();

        Assert.All(ids, id => Assert.True(UserDateRules.IsValidId(id), $"Id '{id}' is invalid."));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void No_embedded_id_uses_the_custom_rule_prefix()
    {
        var ids = RawRules().EnumerateArray().Select(rule => rule.GetProperty("id").GetString()).ToList();

        Assert.NotEmpty(ids);
        Assert.DoesNotContain(ids, id => id!.StartsWith("custom-", StringComparison.Ordinal));
    }

    [Fact]
    public void Embedded_rules_are_returned_on_every_call()
    {
        Assert.Equal(Load(), Load());
    }

    [Theory]
    [InlineData("IMG-20260812-WA0017", "whatsapp-android", 2026, 8, 12, null)]
    [InlineData("IMG-20261110-WA0009", "whatsapp-android", 2026, 11, 10, null)]
    [InlineData("IMG-20260801-WA0030", "whatsapp-android", 2026, 8, 1, null)]
    [InlineData("IMG-20261117-WA0000", "whatsapp-android", 2026, 11, 17, null)]
    [InlineData("IMG-20260924-WA0003", "whatsapp-android", 2026, 9, 24, null)]
    [InlineData("IMG-20160706-WA0002", "whatsapp-android", 2016, 7, 6, null)]
    [InlineData("IMG-20261024-WA0028", "whatsapp-android", 2026, 10, 24, null)]
    [InlineData("VID-20240714-WA0001", "whatsapp-android", 2024, 7, 14, null)]
    [InlineData("WhatsApp Image 2024-07-14 at 15.32.08", "whatsapp-desktop", 2024, 7, 14, "15:32:08")]
    [InlineData("WhatsApp Bild 2024-07-14 um 15.32.08", "whatsapp-desktop", 2024, 7, 14, "15:32:08")]
    [InlineData("WhatsApp Bild 2024-07-14 um 15.32.08_1a2b3c4d", "whatsapp-desktop", 2024, 7, 14, "15:32:08")]
    [InlineData("WhatsApp Video 2024-07-14 at 15.32.08 (1)", "whatsapp-desktop", 2024, 7, 14, "15:32:08")]
    public void Seeded_rules_read_the_expected_dates(string name, string ruleId, int year, int month, int day, string? time)
    {
        var match = new DateRuleSet(Load()).Match(name, Now);

        Assert.NotNull(match);
        Assert.Equal(ruleId, match.RuleId);
        Assert.Equal(new DateOnly(year, month, day), match.Date.Date);
        Assert.Equal(time is null ? null : TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), match.Date.Time);
    }

    [Theory]
    [InlineData("IMG_4200")]
    [InlineData("Screenshot 2024-07-15")]
    [InlineData("IMG-20241332-WA0001")]
    public void Seeded_rules_ignore_other_names(string name)
    {
        Assert.Null(new DateRuleSet(Load()).Match(name, Now));
    }
}
