namespace Snapdate.Infrastructure.FileNames;

/// <summary>JSON shape of one of the user's own rules.</summary>
internal sealed class CustomDateRuleEntry
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>Optional when reading; default true.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Optional when reading; default <c>pattern</c>. Unknown values are kept and written back.</summary>
    public string? Syntax { get; set; }

    public List<string?>? Patterns { get; set; }
}
