using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snapdate.App.Localization;
using Snapdate.Domain.FileNames;

namespace Snapdate.App.ViewModels;

/// <summary>
/// One row of the "Dates from file names" dialog: name, patterns, source badge, on/off switch and the row actions
/// (duplicate for built-in rules, delete with an inline confirmation for the user's own rules).
/// </summary>
public sealed partial class DateRuleItemViewModel : ObservableObject
{
    private readonly DateRulesViewModel _owner;
    private bool _syncing;

    internal DateRuleItemViewModel(DateRulesViewModel owner, DateRule rule, LocalizedStrings l, bool canChange)
    {
        _owner = owner;
        Rule = rule;
        CanChange = canChange;
        _syncing = true;
        IsEnabled = rule.Enabled;
        _syncing = false;
        L = l;
        UpdateStrings(l);
    }

    /// <summary>The rule as shown; replaced when the switch changes.</summary>
    public DateRule Rule { get; private set; }

    public string Id => Rule.Id;

    public string Name => Rule.Name;

    /// <summary>All patterns on one line (the row trims it; the tooltip shows one pattern per line).</summary>
    public string PatternsText => string.Join("   ", Rule.Patterns ?? []);

    public string PatternsTip => string.Join(Environment.NewLine, Rule.Patterns ?? []);

    public bool IsSystem => Rule.Origin == RuleOrigin.System;

    public bool IsCustom => Rule.Origin == RuleOrigin.Custom;

    /// <summary>False for a rule written in a syntax this version cannot use (e.g. by a newer version).</summary>
    public bool IsSyntaxSupported => Rule.IsSyntaxSupported;

    public bool IsSyntaxUnsupported => !Rule.IsSyntaxSupported;

    /// <summary>The syntax name, shown as a small label when this version cannot use it.</summary>
    public string SyntaxLabel => Rule.Syntax;

    /// <summary>False while the user's rules are read-only (newer version, file not accessible).</summary>
    public bool CanChange { get; }

    public bool CanDuplicate => CanChange && IsSystem;

    public bool CanDelete => CanChange && IsCustom;

    /// <summary>Editing arrives with the rule editor (#20); hidden until then.</summary>
    public bool CanEdit => CanChange && IsCustom && DateRulesViewModel.IsEditorAvailable;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>True while the inline "Delete this rule?" confirmation is shown instead of the row actions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool IsConfirmingDelete { get; private set; }

    public bool ShowActions => !IsConfirmingDelete;

    [ObservableProperty]
    public partial string SourceText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceTip { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ToggleName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DuplicateName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DeleteName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EditName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PatternsName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SyntaxTip { get; private set; } = string.Empty;

    /// <summary>Spoken name of the whole row.</summary>
    [ObservableProperty]
    public partial string Accessible { get; private set; } = string.Empty;

    /// <summary>UI strings for the row's buttons.</summary>
    [ObservableProperty]
    public partial LocalizedStrings L { get; private set; }

    internal void UpdateStrings(LocalizedStrings l)
    {
        L = l;
        SourceText = IsSystem ? l.RulesSourceSystem : l.RulesSourceCustom;
        SourceTip = IsSystem ? l.RulesSourceSystemTip : l.RulesSourceCustomTip;
        ToggleName = l.Format(nameof(LocalizedStrings.RuleToggleName), Name);
        DuplicateName = l.Format(nameof(LocalizedStrings.RuleDuplicateName), Name);
        DeleteName = l.Format(nameof(LocalizedStrings.RuleDeleteName), Name);
        EditName = l.Format(nameof(LocalizedStrings.RuleEditName), Name);
        PatternsName = l.Format(nameof(LocalizedStrings.RulePatternsName), string.Join(", ", Rule.Patterns ?? []));
        SyntaxTip = IsSyntaxUnsupported ? l.Format(nameof(LocalizedStrings.RuleSyntaxUnsupported), Rule.Syntax) : string.Empty;
        UpdateAccessible();
    }

    /// <summary>
    /// "&lt;name&gt;, &lt;source&gt;"; while read-only the (disabled) switch is easy to skip, so the row also says
    /// whether the rule is on or off.
    /// </summary>
    private void UpdateAccessible() =>
        Accessible = CanChange
            ? $"{Name}, {SourceText}"
            : $"{Name}, {SourceText}, {(IsEnabled ? L.RuleStateOn : L.RuleStateOff)}";

    /// <summary>Shows the switch state without saving (after a reload or a refused change).</summary>
    internal void ShowEnabled(bool enabled)
    {
        _syncing = true;
        IsEnabled = enabled;
        Rule = Rule with { Enabled = enabled };
        _syncing = false;
        UpdateAccessible();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        Rule = Rule with { Enabled = value };
        _owner.SetEnabled(this, value);
    }

    [RelayCommand]
    private void Duplicate() => _owner.Duplicate(this);

    [RelayCommand]
    private void Delete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsConfirmingDelete = false;
        _owner.Delete(this);
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    /// <summary>Esc while the confirmation is shown: back to the row actions. True when something was cancelled.</summary>
    internal bool CancelPendingDelete()
    {
        if (!IsConfirmingDelete)
        {
            return false;
        }

        IsConfirmingDelete = false;
        return true;
    }
}
