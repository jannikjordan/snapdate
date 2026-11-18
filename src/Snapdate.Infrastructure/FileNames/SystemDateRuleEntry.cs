namespace Snapdate.Infrastructure.FileNames;

/// <summary>JSON shape of one built-in rule. Ids are permanent and never renamed.</summary>
internal sealed class SystemDateRuleEntry
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>Optional; default <c>pattern</c>.</summary>
    public string? Syntax { get; set; }

    public List<string?>? Patterns { get; set; }
}
