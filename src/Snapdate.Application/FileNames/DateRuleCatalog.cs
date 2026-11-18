using System.Security.Cryptography;
using Snapdate.Application.Ports;
using Snapdate.Domain.FileNames;

namespace Snapdate.Application.FileNames;

/// <summary>
/// The system rules combined with the user's changes: the effective <see cref="DateRuleSet"/> and the edit
/// operations of the rules dialog. Custom rules come first (in list order), then system rules (in shipped order).
/// Users never edit system rules; they can switch them off or duplicate them into a custom rule.
/// When the user file was written by a newer version the catalog is read-only and every change is refused.
/// Not thread-safe: owned by one view model.
/// </summary>
public sealed class DateRuleCatalog
{
    private const string CustomIdPrefix = "custom-";
    private const string CopySuffix = " (copy)";

    private readonly List<DateRule> _system;
    private readonly List<DateRule> _custom;
    private readonly List<string> _disabled;

    /// <param name="systemRules">Built-in rules in shipped order.</param>
    /// <param name="user">The user's rules as loaded.</param>
    public DateRuleCatalog(IReadOnlyList<DateRule> systemRules, UserDateRulesLoad user)
    {
        ArgumentNullException.ThrowIfNull(systemRules);
        ArgumentNullException.ThrowIfNull(user);

        _system = [.. systemRules
            .Where(rule => rule is not null)
            .DistinctBy(rule => rule.Id, StringComparer.Ordinal)
            .Select(rule => rule with { Origin = RuleOrigin.System, Enabled = true })];
        Status = user.Status;

        var rules = user.Rules ?? UserDateRules.Empty;
        _disabled = [.. (rules.DisabledSystemRuleIds ?? []).Where(UserDateRules.IsValidId).Distinct(StringComparer.Ordinal)];

        // Ids must be unique across both lists, or switching a rule off would be ambiguous.
        var ids = new HashSet<string>(_system.Select(rule => rule.Id), StringComparer.Ordinal);
        _custom = [.. (rules.CustomRules ?? [])
            .Where(UserDateRules.IsValidRule)
            .Where(rule => ids.Add(rule.Id))
            .Take(UserDateRules.MaxCustomRules)
            .Select(rule => rule with { Origin = RuleOrigin.Custom })];
    }

    /// <summary>What was found when the user's rules were loaded.</summary>
    public UserRulesStatus Status { get; }

    /// <summary>True when a newer version wrote the user's file; nothing may be changed or saved.</summary>
    public bool IsReadOnly => Status == UserRulesStatus.NewerVersion;

    /// <summary>The user's rules in precedence order.</summary>
    public IReadOnlyList<DateRule> CustomRules => _custom.AsReadOnly();

    /// <summary>The built-in rules in shipped order; <see cref="DateRule.Enabled"/> is false for switched-off rules.</summary>
    public IReadOnlyList<DateRule> SystemRules =>
        [.. _system.Select(rule => rule with { Enabled = !_disabled.Contains(rule.Id, StringComparer.Ordinal) })];

    /// <summary>All rules in precedence order: custom rules, then system rules.</summary>
    public IReadOnlyList<DateRule> Rules => [.. _custom, .. SystemRules];

    /// <summary>Loads the system and user rules from <paramref name="store"/>.</summary>
    public static DateRuleCatalog Load(IDateRuleStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new DateRuleCatalog(store.LoadSystemRules(), store.LoadUserRules());
    }

    /// <summary>Saves the user's part to <paramref name="store"/>; false when read-only or not written.</summary>
    public bool Save(IDateRuleStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return !IsReadOnly && store.SaveUserRules(ToUserRules());
    }

    /// <summary>The compiled rule set for scanning.</summary>
    public DateRuleSet ToRuleSet() => new(Rules);

    /// <summary>The user's part, to be saved.</summary>
    public UserDateRules ToUserRules() => new([.. _disabled], [.. _custom]);

    /// <summary>The rule with <paramref name="id"/> (custom or system), or null.</summary>
    public DateRule? Find(string? id) => Rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.Ordinal));

    /// <summary>Switches a custom or system rule on or off. False for an unknown id or when read-only.</summary>
    public bool SetEnabled(string? id, bool enabled)
    {
        if (IsReadOnly)
        {
            return false;
        }

        var customIndex = IndexOfCustom(id);
        if (customIndex >= 0)
        {
            _custom[customIndex] = _custom[customIndex] with { Enabled = enabled };
            return true;
        }

        if (!_system.Exists(rule => string.Equals(rule.Id, id, StringComparison.Ordinal)))
        {
            return false;
        }

        _disabled.RemoveAll(disabled => string.Equals(disabled, id, StringComparison.Ordinal));
        if (!enabled)
        {
            _disabled.Add(id!);
        }

        return true;
    }

    /// <summary>
    /// Appends an enabled custom copy of the rule with <paramref name="id"/>; the original stays as it is.
    /// </summary>
    /// <param name="id">A system or custom rule.</param>
    /// <param name="name">Name of the copy; default "&lt;name&gt; (copy)". The UI passes a localised name.</param>
    /// <returns>The copy, or null for an unknown id, a full list, an invalid name or when read-only.</returns>
    public DateRule? Duplicate(string? id, string? name = null)
    {
        if (Find(id) is not { } original)
        {
            return null;
        }

        return Append(name ?? CopyName(original.Name), original.Patterns, original.Syntax, enabled: true);
    }

    /// <summary>Appends a new enabled custom rule with a fresh id.</summary>
    /// <returns>The new rule, or null when the values are invalid, the list is full or the catalog is read-only.</returns>
    public DateRule? Add(string name, IReadOnlyList<string> patterns, string syntax = DateRuleSyntax.Pattern) =>
        Append(name, patterns, syntax, enabled: true);

    /// <summary>Replaces the custom rule with the same id. False for system rules, unknown ids, invalid values or when read-only.</summary>
    public bool Update(DateRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var index = IndexOfCustom(rule.Id);
        if (IsReadOnly || index < 0 || !UserDateRules.IsValidRule(rule))
        {
            return false;
        }

        _custom[index] = rule with { Origin = RuleOrigin.Custom, Patterns = [.. rule.Patterns] };
        return true;
    }

    /// <summary>Deletes a custom rule. False for system rules, unknown ids or when read-only.</summary>
    public bool Delete(string? id)
    {
        var index = IndexOfCustom(id);
        if (IsReadOnly || index < 0)
        {
            return false;
        }

        _custom.RemoveAt(index);
        return true;
    }

    private DateRule? Append(string name, IReadOnlyList<string> patterns, string syntax, bool enabled)
    {
        if (IsReadOnly || _custom.Count >= UserDateRules.MaxCustomRules)
        {
            return null;
        }

        var rule = new DateRule(NewId(), name, RuleOrigin.Custom, enabled, [.. patterns ?? []], syntax);
        if (!UserDateRules.IsValidRule(rule))
        {
            return null;
        }

        _custom.Add(rule);
        return rule;
    }

    private int IndexOfCustom(string? id) => _custom.FindIndex(rule => string.Equals(rule.Id, id, StringComparison.Ordinal));

    private string NewId()
    {
        while (true)
        {
            var id = CustomIdPrefix + RandomNumberGenerator.GetHexString(8, lowercase: true);
            if (Find(id) is null)
            {
                return id;
            }
        }
    }

    private static string CopyName(string name)
    {
        var baseName = name ?? string.Empty;
        var room = UserDateRules.MaxNameLength - CopySuffix.Length;
        return (baseName.Length > room ? baseName[..room] : baseName) + CopySuffix;
    }
}
