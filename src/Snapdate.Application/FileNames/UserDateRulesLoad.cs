namespace Snapdate.Application.FileNames;

/// <summary>The result of loading the user's rule file.</summary>
/// <param name="Rules">The rules that could be read; empty when the file is missing or unreadable.</param>
/// <param name="Status">What was found.</param>
/// <param name="Fingerprint">
/// What the file looked like when it was read; pass it back to <see cref="Ports.IDateRuleStore.SaveUserRules"/>.
/// Default <see cref="UserRulesFingerprint.Unknown"/>, which never allows a save over an existing or new file.
/// </param>
public sealed record UserDateRulesLoad(UserDateRules Rules, UserRulesStatus Status, UserRulesFingerprint? Fingerprint = null)
{
    /// <summary>No file yet.</summary>
    public static UserDateRulesLoad Missing { get; } = new(UserDateRules.Empty, UserRulesStatus.Missing, UserRulesFingerprint.Absent);

    /// <summary>What the file looked like when it was read.</summary>
    public UserRulesFingerprint Fingerprint { get; init; } = Fingerprint ?? UserRulesFingerprint.Unknown;
}
