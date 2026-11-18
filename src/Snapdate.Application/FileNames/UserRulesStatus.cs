namespace Snapdate.Application.FileNames;

/// <summary>What <see cref="Ports.IDateRuleStore.LoadUserRules"/> found in the user's rule file.</summary>
public enum UserRulesStatus
{
    /// <summary>The file was read (entries with invalid values may have been dropped).</summary>
    Ok,

    /// <summary>There is no file yet; the user has no own rules.</summary>
    Missing,

    /// <summary>The file is too large, not JSON or has wrong types; it is ignored and moved aside before the next save.</summary>
    Unreadable,

    /// <summary>
    /// The file exists but could not be opened (e.g. locked by another program); the rules are unknown, so nothing is
    /// changed or saved until it can be read again.
    /// </summary>
    Inaccessible,

    /// <summary>A newer version wrote the file; its rules are used best effort, but it is never saved.</summary>
    NewerVersion,
}
