using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snapdate.App.Localization;
using Snapdate.Application.FileNames;
using Snapdate.Domain.FileNames;

namespace Snapdate.App.ViewModels;

/// <summary>
/// The rule editor inside "Dates from file names": name, patterns (one per line) with blocks that insert at the
/// cursor, a plain-language message for every line that cannot be used, and a tester for one file name.
/// Saving goes through the owning <see cref="DateRulesViewModel"/>.
/// </summary>
public sealed partial class DateRuleEditorViewModel : ObservableObject
{
    private readonly DateRulesViewModel _owner;
    private readonly TimeProvider _clock;
    private readonly string _initialName;
    private readonly string _initialPatterns;
    private LocalizedStrings _l;
    private IReadOnlyList<Line> _lines = [];

    /// <param name="owner">The dialog that opened the editor and saves the rule.</param>
    /// <param name="l">UI strings.</param>
    /// <param name="clock">"Now" for the plausibility check of the tester.</param>
    /// <param name="rule">The user's rule to edit; null for a new rule.</param>
    /// <param name="patterns">Pattern text to start with for a new rule (e.g. suggested from a file name).</param>
    /// <param name="sample">File name to try at first (from the dialog's tester).</param>
    /// <param name="sampleNames">Names from the current folder for the picker.</param>
    internal DateRuleEditorViewModel(
        DateRulesViewModel owner,
        LocalizedStrings l,
        TimeProvider clock,
        DateRule? rule,
        string? patterns,
        string? sample,
        IReadOnlyList<string> sampleNames)
    {
        _owner = owner;
        _l = l;
        L = l;
        _clock = clock;
        RuleId = rule?.Id;
        Syntax = rule?.Syntax ?? DateRuleSyntax.Pattern;
        SampleNames = sampleNames;
        _initialName = rule?.Name ?? string.Empty;
        _initialPatterns = rule is null ? patterns ?? string.Empty : string.Join(Environment.NewLine, rule.Patterns ?? []);
        Name = _initialName;
        PatternsText = _initialPatterns;
        CaretIndex = PatternsText.Length;
        Sample = sample ?? string.Empty;
        UpdateStrings(l);
    }

    /// <summary>The id of the rule being edited; null for a new rule.</summary>
    public string? RuleId { get; }

    public bool IsNew => RuleId is null;

    /// <summary>How the patterns are written. The validation and the tester use it, so a second syntax plugs in here.</summary>
    public string Syntax { get; }

    [ObservableProperty]
    public partial LocalizedStrings L { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PatternsLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>One pattern per line; blank lines are ignored.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string PatternsText { get; set; } = string.Empty;

    /// <summary>Caret position in the pattern box, so blocks are inserted where the user is typing.</summary>
    [ObservableProperty]
    public partial int CaretIndex { get; set; }

    /// <summary>Blocks that insert a placeholder (or <c>*</c>) at the caret.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ChipViewModel> Chips { get; private set; } = [];

    /// <summary>A message per line that cannot be used (and for too many or no patterns).</summary>
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems), nameof(ShowPatternsOk))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsValid { get; private set; }

    public bool HasProblems => !IsValid;

    public bool ShowPatternsOk => IsValid;

    /// <summary>All problems in one text, or "All patterns can be used." (read by screen readers as a polite live region).</summary>
    [ObservableProperty]
    public partial string ValidationText { get; private set; } = string.Empty;

    /// <summary>The name that will be saved: trimmed, "My rule" when empty.</summary>
    public string EffectiveName => Name.Trim() is { Length: > 0 } trimmed ? trimmed : _l.RuleDefaultName;

    /// <summary>The patterns that will be saved: every non-blank line, trimmed.</summary>
    public IReadOnlyList<string> Patterns => [.. _lines.Where(line => line.Text.Length > 0).Select(line => line.Text)];

    // ---------- Tester ----------

    /// <summary>File name to try (with or without extension).</summary>
    [ObservableProperty]
    public partial string Sample { get; set; } = string.Empty;

    public IReadOnlyList<string> SampleNames { get; }

    public bool HasSampleNames => SampleNames.Count > 0;

    /// <summary>The name picked from <see cref="SampleNames"/>; copied into <see cref="Sample"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedSample { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSampleMatch), nameof(IsSampleNotADate), nameof(IsSampleNoMatch), nameof(HasSampleIcon))]
    public partial RuleSampleKind SampleKind { get; private set; }

    public bool IsSampleMatch => SampleKind == RuleSampleKind.Match;

    public bool IsSampleNotADate => SampleKind == RuleSampleKind.NotADate;

    public bool IsSampleNoMatch => SampleKind == RuleSampleKind.NoMatch;

    public bool HasSampleIcon => SampleKind != RuleSampleKind.Empty;

    /// <summary>What the tester found, in plain words (a polite live region).</summary>
    [ObservableProperty]
    public partial string SampleResult { get; private set; } = string.Empty;

    // ---------- Leaving ----------

    /// <summary>True while "Discard your changes?" is shown instead of Cancel and Save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowButtons))]
    public partial bool IsConfirmingDiscard { get; private set; }

    public bool ShowButtons => !IsConfirmingDiscard;

    /// <summary>True when name or patterns differ from what the editor started with.</summary>
    public bool IsDirty =>
        !string.Equals(Name, _initialName, StringComparison.Ordinal)
        || !string.Equals(Normalise(PatternsText), Normalise(_initialPatterns), StringComparison.Ordinal);

    internal void UpdateStrings(LocalizedStrings l)
    {
        _l = l;
        L = l;
        Title = IsNew ? l.RuleNewTitle : l.RuleEditTitle;
        PatternsLabel = l.Format(nameof(LocalizedStrings.RulePatternsLabel), DateRule.MaxPatterns);
        Chips =
        [
            Chip(l.TokYear, "{YYYY}"),
            Chip(l.TokMonth, "{MM}"),
            Chip(l.TokDay, "{DD}"),
            Chip(l.TokHour, "{HH}"),
            Chip(l.TokMinute, "{mm}"),
            Chip(l.TokSecond, "{ss}"),
            new ChipViewModel(l.TokAnyText, l.TokAnyTextTip, new RelayCommand(() => Insert("*")), IsToken: false),
        ];
        Validate();
    }

    /// <summary>Inserts text (a placeholder or <c>*</c>) at the caret.</summary>
    public void Insert(string snippet)
    {
        var caret = Math.Clamp(CaretIndex, 0, PatternsText.Length);
        PatternsText = PatternsText.Insert(caret, snippet);
        CaretIndex = caret + snippet.Length;
    }

    /// <summary>Esc: answers an open "Discard?" with "keep editing", otherwise acts like Cancel.</summary>
    internal void Escape()
    {
        if (IsConfirmingDiscard)
        {
            IsConfirmingDiscard = false;
        }
        else
        {
            Cancel();
        }
    }

    private bool CanSave() => IsValid && UserDateRules.IsValidName(EffectiveName);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (CanSave())
        {
            IsConfirmingDiscard = false;
            _owner.SaveEditor(this);
        }
    }

    /// <summary>Leaves the editor; asks first when something was changed.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsDirty)
        {
            IsConfirmingDiscard = true;
        }
        else
        {
            _owner.CloseEditor();
        }
    }

    [RelayCommand]
    private void Discard()
    {
        IsConfirmingDiscard = false;
        _owner.CloseEditor();
    }

    [RelayCommand]
    private void KeepEditing() => IsConfirmingDiscard = false;

    partial void OnNameChanged(string value) => Validate();

    partial void OnPatternsTextChanged(string value) => Validate();

    partial void OnSampleChanged(string value)
    {
        // Once the text was edited the picker shows nothing, so picking the same name again fills it in again.
        if (SelectedSample is not null && !string.Equals(SelectedSample, value, StringComparison.Ordinal))
        {
            SelectedSample = null;
        }

        UpdateSample();
    }

    partial void OnSelectedSampleChanged(string? value)
    {
        if (value is not null)
        {
            Sample = value;
        }
    }

    private ChipViewModel Chip(string label, string token) =>
        new(label, token, new RelayCommand(() => Insert(token)), IsToken: true);

    private void Validate()
    {
        _lines = [.. (PatternsText ?? string.Empty)
            .Split('\n')
            .Select((text, index) => Compile(index + 1, text.Trim()))];

        Problems.Clear();
        var used = _lines.Where(line => line.Text.Length > 0).ToList();
        if (used.Count == 0)
        {
            Problems.Add(_l.Format(nameof(LocalizedStrings.RuleNoPatterns)));
        }
        else if (used.Count > DateRule.MaxPatterns)
        {
            Problems.Add(_l.Format(nameof(LocalizedStrings.RuleTooManyPatterns), DateRule.MaxPatterns, used.Count - DateRule.MaxPatterns));
        }

        foreach (var line in used.Where(line => line.Error is not null))
        {
            Problems.Add(_l.Format(nameof(LocalizedStrings.RuleProblemLine), line.Number, Describe(line.Error!)));
        }

        if (Name.Trim().Length > UserDateRules.MaxNameLength)
        {
            Problems.Add(_l.Format(nameof(LocalizedStrings.RuleNameTooLong), UserDateRules.MaxNameLength));
        }

        IsValid = Problems.Count == 0;
        ValidationText = IsValid ? _l.RulePatternsOk : string.Join(Environment.NewLine, Problems);
        UpdateSample();
    }

    /// <summary>
    /// Parses one line in <see cref="Syntax"/>: the first error, or the compiled pattern. The only place that knows
    /// how a pattern is written; a further syntax adds a case here.
    /// </summary>
    private Line Compile(int number, string text)
    {
        if (text.Length == 0)
        {
            return new Line(number, text, null, null);
        }

        switch (Syntax)
        {
            case DateRuleSyntax.Pattern:
                var parsed = DatePattern.Parse(text);
                return parsed.Pattern is { } pattern
                    ? new Line(number, text, null, pattern.Check)
                    : new Line(number, text, parsed.Errors[0], null);
            default:
                return new Line(number, text, new DatePatternError(DatePatternErrorKind.InvalidExpression, 0), null);
        }
    }

    /// <summary>A plain-language explanation for every <see cref="DatePatternErrorKind"/>.</summary>
    internal string Describe(DatePatternError error) => error.Kind switch
    {
        DatePatternErrorKind.Empty => _l.PatErrEmpty,
        DatePatternErrorKind.TooLong => _l.Format(nameof(LocalizedStrings.PatErrTooLong), DatePattern.MaxLength),
        DatePatternErrorKind.UnclosedBrace => _l.Format(nameof(LocalizedStrings.PatErrUnclosedBrace)),
        DatePatternErrorKind.UnexpectedClosingBrace => _l.Format(nameof(LocalizedStrings.PatErrClosingBrace)),
        DatePatternErrorKind.UnknownToken => _l.Format(nameof(LocalizedStrings.PatErrUnknownToken), error.Detail),
        DatePatternErrorKind.UnsupportedToken => _l.Format(nameof(LocalizedStrings.PatErrUnsupportedToken), error.Detail),
        DatePatternErrorKind.DuplicateToken => _l.Format(nameof(LocalizedStrings.PatErrDuplicateToken), error.Detail),
        DatePatternErrorKind.MissingYear => _l.Format(nameof(LocalizedStrings.PatErrMissingYear)),
        DatePatternErrorKind.MissingMonth => _l.Format(nameof(LocalizedStrings.PatErrMissingMonth)),
        DatePatternErrorKind.MissingDay => _l.Format(nameof(LocalizedStrings.PatErrMissingDay)),
        DatePatternErrorKind.HourWithoutMinute => _l.Format(nameof(LocalizedStrings.PatErrHourWithoutMinute)),
        DatePatternErrorKind.MinuteWithoutHour => _l.Format(nameof(LocalizedStrings.PatErrMinuteWithoutHour)),
        DatePatternErrorKind.SecondWithoutMinute => _l.Format(nameof(LocalizedStrings.PatErrSecondWithoutMinute)),
        DatePatternErrorKind.AmbiguousNeighbours => _l.Format(nameof(LocalizedStrings.PatErrAmbiguous), error.Detail),
        _ => _l.PatErrInvalid,
    };

    private void UpdateSample()
    {
        var name = DateRulesViewModel.NameWithoutExtension(Sample);
        var usable = _lines.Where(line => line.Check is not null).Take(DateRule.MaxPatterns).ToList();
        if (name.Length == 0 || usable.Count == 0)
        {
            SampleKind = RuleSampleKind.Empty;
            SampleResult = name.Length == 0 ? _l.RuleSampleEmpty : _l.RuleSampleNoPatterns;
            return;
        }

        // Like scanning: the first line that gives a date wins. Otherwise explain the first line that fits.
        var now = _clock.GetLocalNow().DateTime;
        var checks = usable.Select(line => (line.Number, Result: line.Check!(name, now))).ToList();
        if (checks.FirstOrDefault(c => c.Result.Kind == PatternCheckKind.Date) is { Result: not null } match)
        {
            SampleKind = RuleSampleKind.Match;
            SampleResult = _l.Format(nameof(LocalizedStrings.RuleSampleMatch), match.Number, DateRulesViewModel.FormatDate(_l, match.Result.Date!.Value));
        }
        else if (checks.FirstOrDefault(c => c.Result.Kind != PatternCheckKind.NoMatch) is { Result: not null } fits)
        {
            SampleKind = RuleSampleKind.NotADate;
            SampleResult = _l.Format(
                fits.Result.Kind == PatternCheckKind.ImplausibleDate
                    ? nameof(LocalizedStrings.RuleSampleImplausible)
                    : nameof(LocalizedStrings.RuleSampleInvalid),
                fits.Number,
                fits.Result.Found);
        }
        else
        {
            SampleKind = RuleSampleKind.NoMatch;
            SampleResult = _l.RuleSampleNoMatch;
        }
    }

    private static string Normalise(string text) =>
        string.Join('\n', text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));

    /// <summary>One line of the pattern box: its 1-based number, trimmed text and either an error or a tester.</summary>
    private sealed record Line(int Number, string Text, DatePatternError? Error, Func<string, DateTime, PatternCheck>? Check);
}
