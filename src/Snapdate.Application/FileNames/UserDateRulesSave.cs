namespace Snapdate.Application.FileNames;

/// <summary>The result of <see cref="Ports.IDateRuleStore.SaveUserRules"/>.</summary>
/// <param name="Result">What happened.</param>
/// <param name="Fingerprint">
/// The fingerprint of the file just written when <paramref name="Result"/> is <see cref="DateRuleSaveResult.Saved"/>;
/// <see cref="UserRulesFingerprint.Absent"/> when the result is <see cref="DateRuleSaveResult.Failed"/> after an
/// unreadable file was moved aside (there is no file any more); otherwise the fingerprint that was passed in, unchanged.
/// </param>
public sealed record UserDateRulesSave(DateRuleSaveResult Result, UserRulesFingerprint Fingerprint);
