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

    /// <summary>A newer version wrote the file; its rules are used best effort, but it is never saved.</summary>
    NewerVersion,
}
