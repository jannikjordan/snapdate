namespace Snapdate.Infrastructure.FileNames;

/// <summary>JSON shape of the embedded <c>system-date-rules.json</c> (schema v1).</summary>
internal sealed class SystemDateRulesFile
{
    public int? SchemaVersion { get; set; }

    public List<SystemDateRuleEntry?>? Rules { get; set; }
}
