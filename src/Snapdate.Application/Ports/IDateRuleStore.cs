using Snapdate.Application.FileNames;
using Snapdate.Domain.FileNames;

namespace Snapdate.Application.Ports;

/// <summary>
/// Loads the rules that ship with the app and loads and saves the user's own rules. No member ever throws:
/// a broken user file never breaks the app or the built-in rules.
/// </summary>
public interface IDateRuleStore
{
    /// <summary>The built-in rules in shipped order, all enabled; empty if they cannot be read.</summary>
    IReadOnlyList<DateRule> LoadSystemRules();

    /// <summary>The user's rules and what was found (missing, unreadable, written by a newer version).</summary>
    UserDateRulesLoad LoadUserRules();

    /// <summary>
    /// Saves the user's rules. Returns false when nothing was written: the file on disk was written by a newer
    /// version, or it could not be written. An unreadable file is moved aside first, never overwritten.
    /// </summary>
    bool SaveUserRules(UserDateRules rules);
}
