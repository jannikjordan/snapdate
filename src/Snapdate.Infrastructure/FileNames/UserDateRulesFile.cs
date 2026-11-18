namespace Snapdate.Infrastructure.FileNames;

/// <summary>JSON shape of the user's <c>date-rules.json</c> (schema v1).</summary>
internal sealed class UserDateRulesFile
{
    public int SchemaVersion { get; set; }

    public List<string?>? DisabledSystemRules { get; set; }

    public List<CustomDateRuleEntry?>? CustomRules { get; set; }
}
