using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snapdate.App.Localization;
using Snapdate.Application.FileNames;
using Snapdate.Application.Ports;
using Snapdate.Application.Scanning;
using Snapdate.Domain.FileNames;

namespace Snapdate.App.ViewModels;

/// <summary>The kind of result the tester shows; the view picks icon and colour from it.</summary>
public enum RuleTestKind
{
    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>An enabled rule reads a date.</summary>
    Match,

    /// <summary>Only a switched-off rule would read a date.</summary>
    MatchSwitchedOff,

    /// <summary>No rule reads a date.</summary>
    NoMatch,
}

/// <summary>
/// The "Dates from file names" dialog: every rule with its on/off switch, duplicate and delete, and a tester for a
/// file name. Every change is saved at once through <see cref="DateRuleCatalog"/>; when the dialog closes after a
/// change, <see cref="Closed"/> reports it so the folder is scanned again with the new rules.
/// </summary>
public sealed partial class DateRulesViewModel : ObservableObject
{
    /// <summary>The picker offers at most this many names from the current folder.</summary>
    public const int MaxSampleNames = 50;

    private readonly IDateRuleStore _store;
    private readonly TimeProvider _clock;
    private LocalizedStrings _l;
    private bool _changed;
    private DateRuleSet _effective;
    private DateRuleSet _allSwitchedOn;

    public DateRulesViewModel(IDateRuleStore store, LocalizedStrings l, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _l = l;
        L = l;
        _clock = clock;
        Catalog = DateRuleCatalog.Load(store);
        _effective = Catalog.ToRuleSet();
        _allSwitchedOn = AllSwitchedOn(Catalog);
        RebuildItems();
        UpdateStatus();
        UpdateTestResult();
    }

    /// <summary>Raised when the dialog closes; the argument is true when the rules changed while it was open.</summary>
    public event EventHandler<bool>? Closed;

    /// <summary>The user's and the built-in rules; replaced when the file had to be reloaded.</summary>
    public DateRuleCatalog Catalog { get; private set; }

    /// <summary>The rule set for scanning, in precedence order.</summary>
    public DateRuleSet RuleSet => _effective;

    [ObservableProperty]
    public partial LocalizedStrings L { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Rules in precedence order: the user's own first, then the built-in ones.</summary>
    public ObservableCollection<DateRuleItemViewModel> Items { get; } = [];

    /// <summary>False while the user's rules are read-only (file written by a newer version, or not accessible).</summary>
    public bool CanChange => !Catalog.IsReadOnly;

    /// <summary>Adding rules arrives with the rule editor (#20); hidden until then.</summary>
    public bool CanAdd => CanChange && IsEditorAvailable;

    /// <summary>The rule editor (#20) is not there yet: "Edit" and "Add rule" stay hidden.</summary>
    internal static bool IsEditorAvailable => false;

    /// <summary>Lasting state of the user's file (unreadable, newer version, not accessible); empty when all is well.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; private set; } = string.Empty;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>Result of the last change when it could not be saved; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string MessageText { get; private set; } = string.Empty;

    public bool HasMessage => MessageText.Length > 0;

    // ---------- Tester ----------

    /// <summary>File name to try (with or without extension).</summary>
    [ObservableProperty]
    public partial string TestName { get; set; } = string.Empty;

    /// <summary>Up to <see cref="MaxSampleNames"/> names from the current folder, those with a file date first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSampleNames))]
    public partial IReadOnlyList<string> SampleNames { get; private set; } = [];

    public bool HasSampleNames => SampleNames.Count > 0;

    /// <summary>The name picked from <see cref="SampleNames"/>; copied into <see cref="TestName"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedSample { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTestMatch), nameof(IsTestSwitchedOff), nameof(IsTestNoMatch), nameof(HasTestResult))]
    public partial RuleTestKind TestKind { get; private set; }

    public bool HasTestResult => TestKind != RuleTestKind.Empty;

    public bool IsTestMatch => TestKind == RuleTestKind.Match;

    public bool IsTestSwitchedOff => TestKind == RuleTestKind.MatchSwitchedOff;

    public bool IsTestNoMatch => TestKind == RuleTestKind.NoMatch;

    /// <summary>What the tester found, in plain words (also read by screen readers as a polite live region).</summary>
    [ObservableProperty]
    public partial string TestResult { get; private set; } = string.Empty;

    /// <summary>The id of the rule that should get keyboard focus after the list was rebuilt; null for none.</summary>
    public string? FocusRuleId { get; private set; }

    /// <summary>Opens the dialog.</summary>
    /// <param name="sampleNames">File names (with extension) from the current folder, those with a file date first.</param>
    public void Open(IEnumerable<string>? sampleNames)
    {
        SampleNames = [.. (sampleNames ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxSampleNames)];
        SelectedSample = null;
        MessageText = string.Empty;
        _changed = false;
        foreach (var item in Items)
        {
            item.CancelPendingDelete();
        }

        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        foreach (var item in Items)
        {
            item.CancelPendingDelete();
        }

        IsOpen = false;
        var changed = _changed;
        _changed = false;
        Closed?.Invoke(this, changed);
    }

    /// <summary>Esc: cancels an open delete confirmation first, otherwise closes the dialog.</summary>
    public void Escape()
    {
        if (!Items.Any(item => item.CancelPendingDelete()))
        {
            Close();
        }
    }

    internal void UpdateStrings(LocalizedStrings l)
    {
        _l = l;
        L = l;
        foreach (var item in Items)
        {
            item.UpdateStrings(l);
        }

        UpdateStatus();
        MessageText = string.Empty;
        UpdateTestResult();
    }

    /// <summary>Clears the focus request once the view has handled it.</summary>
    internal void ClearFocusRequest() => FocusRuleId = null;

    // ---------- Changes (called by the rows) ----------

    internal void SetEnabled(DateRuleItemViewModel item, bool enabled)
    {
        if (!Catalog.SetEnabled(item.Id, enabled))
        {
            // Read-only or unknown: show the real state again.
            item.ShowEnabled(Catalog.Find(item.Id)?.Enabled ?? !enabled);
            MessageText = _l.RulesReadOnly;
            return;
        }

        if (Save(item))
        {
            RefreshRuleSets();
        }
    }

    internal void Duplicate(DateRuleItemViewModel item)
    {
        if (Catalog.IsReadOnly)
        {
            MessageText = _l.RulesReadOnly;
            return;
        }

        if (Catalog.CustomRules.Count >= UserDateRules.MaxCustomRules)
        {
            MessageText = _l.Format(nameof(LocalizedStrings.RulesFull), UserDateRules.MaxCustomRules);
            return;
        }

        if (Catalog.Duplicate(item.Id, CopyName(item.Name)) is not { } copy)
        {
            return;
        }

        if (Save(item))
        {
            FocusRuleId = copy.Id;
            RebuildItems();
            RefreshRuleSets();
        }
    }

    internal void Delete(DateRuleItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (!Catalog.Delete(item.Id))
        {
            MessageText = _l.RulesReadOnly;
            return;
        }

        if (Save(item))
        {
            // Keep the keyboard near where the deleted row was.
            var rules = Catalog.Rules;
            FocusRuleId = rules.Count == 0 ? null : rules[Math.Clamp(index, 0, rules.Count - 1)].Id;
            RebuildItems();
            RefreshRuleSets();
        }
    }

    /// <summary>"&lt;name&gt; (copy)", shortened so it stays a valid rule name.</summary>
    private string CopyName(string name)
    {
        var copy = _l.Format(nameof(LocalizedStrings.RuleCopyName), name);
        if (copy.Length <= UserDateRules.MaxNameLength)
        {
            return copy;
        }

        var suffix = copy.Length - name.Length;
        var room = Math.Max(1, UserDateRules.MaxNameLength - suffix - 1);
        return _l.Format(nameof(LocalizedStrings.RuleCopyName), name[..Math.Min(room, name.Length)].TrimEnd() + "…");
    }

    /// <summary>
    /// Saves the catalog. When that fails the file was not changed: the rules are read again, so the list shows what
    /// is really stored, and the user is told why. The keyboard stays on <paramref name="item"/>'s row (or the row now
    /// in its place).
    /// </summary>
    private bool Save(DateRuleItemViewModel item)
    {
        var result = Catalog.Save(_store);
        if (result == DateRuleSaveResult.Saved)
        {
            _changed = true;
            MessageText = string.Empty;
            UpdateStatus();
            return true;
        }

        var index = Items.IndexOf(item);
        // Only a conflict means the file changed elsewhere; otherwise the reloaded rules are the ones scanned already.
        Reload(changedElsewhere: result == DateRuleSaveResult.Conflict, focusId: item.Id, focusIndex: index);
        MessageText = result switch
        {
            DateRuleSaveResult.Conflict => _l.RulesConflict,
            DateRuleSaveResult.TooLarge => _l.RulesTooLarge,
            DateRuleSaveResult.ReadOnly => _l.RulesReadOnly,
            _ => _l.RulesSaveFailed,
        };
        return false;
    }

    private void Reload(bool changedElsewhere, string focusId, int focusIndex)
    {
        Catalog = DateRuleCatalog.Load(_store);
        if (changedElsewhere)
        {
            // Another window changed the rules: scan again on close.
            _changed = true;
        }

        var rules = Catalog.Rules;
        FocusRuleId = rules.Any(rule => string.Equals(rule.Id, focusId, StringComparison.Ordinal))
            ? focusId
            : rules.Count == 0 ? null : rules[Math.Clamp(focusIndex, 0, rules.Count - 1)].Id;
        OnPropertyChanged(nameof(Catalog));
        OnPropertyChanged(nameof(CanChange));
        RebuildItems();
        RefreshRuleSets();
        UpdateStatus();
    }

    private void RebuildItems()
    {
        Items.Clear();
        var canChange = !Catalog.IsReadOnly;
        foreach (var rule in Catalog.Rules)
        {
            Items.Add(new DateRuleItemViewModel(this, rule, _l, canChange));
        }
    }

    private void RefreshRuleSets()
    {
        _effective = Catalog.ToRuleSet();
        _allSwitchedOn = AllSwitchedOn(Catalog);
        UpdateTestResult();
    }

    private void UpdateStatus() =>
        StatusText = Catalog.Status switch
        {
            UserRulesStatus.Unreadable => _l.RulesUnreadable,
            UserRulesStatus.NewerVersion => _l.RulesNewerVersion,
            UserRulesStatus.Inaccessible => _l.RulesInaccessible,
            _ => string.Empty,
        };

    private static DateRuleSet AllSwitchedOn(DateRuleCatalog catalog) =>
        new(catalog.Rules.Select(rule => rule with { Enabled = true }));

    // ---------- Tester ----------

    partial void OnTestNameChanged(string value)
    {
        // Once the text was edited the picker shows nothing, so picking the same name again fills it in again.
        if (SelectedSample is not null && !string.Equals(SelectedSample, value, StringComparison.Ordinal))
        {
            SelectedSample = null;
        }

        UpdateTestResult();
    }

    partial void OnSelectedSampleChanged(string? value)
    {
        if (value is not null)
        {
            TestName = value;
        }
    }

    private void UpdateTestResult()
    {
        var name = NameWithoutExtension(TestName);
        if (name.Length == 0)
        {
            TestKind = RuleTestKind.Empty;
            TestResult = _l.TesterEmpty;
            return;
        }

        var now = _clock.GetLocalNow().DateTime;
        if (_effective.Match(name, now) is { } match)
        {
            TestKind = RuleTestKind.Match;
            TestResult = _l.Format(nameof(LocalizedStrings.TesterMatch), match.RuleName, FormatDate(match.Date));
        }
        else if (_allSwitchedOn.Match(name, now) is { } off)
        {
            TestKind = RuleTestKind.MatchSwitchedOff;
            TestResult = _l.Format(nameof(LocalizedStrings.TesterMatchOff), off.RuleName, FormatDate(off.Date));
        }
        else
        {
            TestKind = RuleTestKind.NoMatch;
            TestResult = _l.TesterNoMatch;
        }
    }

    private string FormatDate(FileNameDate date)
    {
        var day = date.Date.ToString("D", _l.Culture);
        return date.Time is { } time
            ? _l.Format(nameof(LocalizedStrings.TesterDateTime), day, time.ToString("T", _l.Culture))
            : day;
    }

    /// <summary>
    /// Rules match the name without its extension. Only known photo and video extensions are removed, so
    /// "WhatsApp Image 2024-07-14 at 15.32.08" keeps its seconds.
    /// </summary>
    internal static string NameWithoutExtension(string? text)
    {
        var name = (text ?? string.Empty).Trim();
        var extension = Path.GetExtension(name);
        return extension.Length > 0 && SupportedFormats.IsSupported(name) ? name[..^extension.Length] : name;
    }
}
