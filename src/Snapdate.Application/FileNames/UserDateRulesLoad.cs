namespace Snapdate.Application.FileNames;

/// <summary>The result of loading the user's rule file.</summary>
/// <param name="Rules">The rules that could be read; empty when the file is missing or unreadable.</param>
/// <param name="Status">What was found.</param>
public sealed record UserDateRulesLoad(UserDateRules Rules, UserRulesStatus Status)
{
    /// <summary>No file yet.</summary>
    public static UserDateRulesLoad Missing { get; } = new(UserDateRules.Empty, UserRulesStatus.Missing);
}
