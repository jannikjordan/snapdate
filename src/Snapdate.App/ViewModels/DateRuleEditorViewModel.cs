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
    private readonly bool _initialIsRegex;
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
        _initialIsRegex = string.Equals(rule?.Syntax, DateRuleSyntax.Regex, StringComparison.Ordinal);
        IsRegex = _initialIsRegex;
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

    /// <summary>How the patterns are written (<see cref="DateRuleSyntax"/>); follows <see cref="IsRegex"/>.</summary>
    public string Syntax => IsRegex ? DateRuleSyntax.Regex : DateRuleSyntax.Pattern;

    /// <summary>
    /// "Advanced: regular expressions". Switching keeps the text as it is and checks it again in the other syntax,
    /// so nothing is lost and switching back restores the previous state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Syntax), nameof(IsPattern))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsRegex { get; set; }

    /// <summary>True for token patterns: the blocks and the pattern hint are shown.</summary>
    public bool IsPattern => !IsRegex;

    /// <summary>The sentence under the title ("Snapdate tries the patterns …" or "… the expressions …").</summary>
    [ObservableProperty]
    public partial string Intro { get; private set; } = string.Empty;

    /// <summary>Help text under the box: the pattern hint, or how to write a regular expression.</summary>
    [ObservableProperty]
    public partial string PatternsHint { get; private set; } = string.Empty;

    /// <summary>The regular-expression example shown under the hint in regex mode.</summary>
    [ObservableProperty]
    public partial string RegexExample { get; private set; } = string.Empty;

    /// <summary>Screen-reader name of the example ("Example: …").</summary>
    [ObservableProperty]
    public partial string RegexExampleName { get; private set; } = string.Empty;

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

    /// <summary>"All patterns can be used." (or "All regular expressions …"), shown with a check icon.</summary>
    [ObservableProperty]
    public partial string PatternsOkText { get; private set; } = string.Empty;

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

    /// <summary>True when name, patterns or syntax differ from what the editor started with.</summary>
    public bool IsDirty =>
        !string.Equals(Name, _initialName, StringComparison.Ordinal)
        || IsRegex != _initialIsRegex
        || !string.Equals(Normalise(PatternsText), Normalise(_initialPatterns), StringComparison.Ordinal);

    internal void UpdateStrings(LocalizedStrings l)
    {
        _l = l;
        L = l;
        Title = IsNew ? l.RuleNewTitle : l.RuleEditTitle;
        RegexExample = l.Format(nameof(LocalizedStrings.RuleRegexExample));
        RegexExampleName = l.Format(nameof(LocalizedStrings.RuleRegexExampleName), RegexExample);
        UpdateSyntaxTexts();
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

    partial void OnIsRegexChanged(bool value)
    {
        UpdateSyntaxTexts();
        Validate();
    }

    /// <summary>Label and hint for the current syntax.</summary>
    private void UpdateSyntaxTexts()
    {
        PatternsLabel = _l.Format(
            IsRegex ? nameof(LocalizedStrings.RuleRegexLabel) : nameof(LocalizedStrings.RulePatternsLabel),
            DateRule.MaxPatterns);
        PatternsHint = IsRegex ? _l.RuleRegexHint : _l.RulePatternsHint;
        Intro = IsRegex ? _l.RuleRegexEditorIntro : _l.RuleEditorIntro;
    }

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
            .Select((text, index) => Compile(index + 1, Clean(text)))];

        Problems.Clear();
        var used = _lines.Where(line => line.Text.Length > 0).ToList();
        if (used.Count == 0)
        {
            Problems.Add(IsRegex
                ? _l.Format(nameof(LocalizedStrings.RuleRegexNoPatterns), RegexExample)
                : _l.Format(nameof(LocalizedStrings.RuleNoPatterns)));
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
        PatternsOkText = IsRegex ? _l.RuleRegexOk : _l.RulePatternsOk;
        ValidationText = IsValid ? PatternsOkText : string.Join(Environment.NewLine, Problems);
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
            case DateRuleSyntax.Regex:
                var compiled = DateExpression.Parse(text);
                return compiled.Expression is { } expression
                    ? new Line(number, text, null, expression.Check)
                    : new Line(number, text, compiled.Errors[0], null);
            default:
                return new Line(number, text, new DatePatternError(DatePatternErrorKind.InvalidExpression, 0), null);
        }
    }

    /// <summary>
    /// A plain-language explanation for every <see cref="DatePatternErrorKind"/>, in the words of the current
    /// <see cref="Syntax"/> (placeholders such as {MM} for patterns, groups such as (?&lt;month&gt;…) for expressions).
    /// </summary>
    internal string Describe(DatePatternError error) => IsRegex ? DescribeRegex(error) : DescribePattern(error);

    private string DescribeRegex(DatePatternError error) => error.Kind switch
    {
        DatePatternErrorKind.TooLong => _l.Format(nameof(LocalizedStrings.RxErrTooLong), DateExpression.MaxLength),
        DatePatternErrorKind.MissingYear => _l.Format(nameof(LocalizedStrings.RxErrMissingYear)),
        DatePatternErrorKind.MissingMonth => _l.Format(nameof(LocalizedStrings.RxErrMissingMonth)),
        DatePatternErrorKind.MissingDay => _l.Format(nameof(LocalizedStrings.RxErrMissingDay)),
        DatePatternErrorKind.HourWithoutMinute => _l.Format(nameof(LocalizedStrings.RxErrHourWithoutMinute)),
        DatePatternErrorKind.MinuteWithoutHour => _l.Format(nameof(LocalizedStrings.RxErrMinuteWithoutHour)),
        DatePatternErrorKind.SecondWithoutMinute => _l.Format(nameof(LocalizedStrings.RxErrSecondWithoutMinute)),
        DatePatternErrorKind.InvalidExpression => _l.RxErrInvalid,
        _ => DescribePattern(error),
    };

    private string DescribePattern(DatePatternError error) => error.Kind switch
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
        // Only regular expressions produce these; worded for them whatever the current syntax.
        DatePatternErrorKind.InvalidSyntax => _l.Format(nameof(LocalizedStrings.RxErrSyntax), Math.Max(1, error.Position)),
        DatePatternErrorKind.UnsupportedConstruct => _l.Format(nameof(LocalizedStrings.RxErrUnsupported)),
        DatePatternErrorKind.RepetitionTooLarge => _l.Format(nameof(LocalizedStrings.RxErrRepetition), error.Detail, DateExpression.MaxRepetition),
        DatePatternErrorKind.UnknownGroup => _l.Format(nameof(LocalizedStrings.RxErrUnknownGroup), error.Detail),
        DatePatternErrorKind.NestedQuantifier => _l.Format(nameof(LocalizedStrings.RxErrNested), error.Detail),
        DatePatternErrorKind.TooComplex => _l.Format(nameof(LocalizedStrings.RxErrTooComplex)),
        DatePatternErrorKind.FreeSpacing => _l.Format(nameof(LocalizedStrings.RxErrFreeSpacing)),
        _ => _l.PatErrInvalid,
    };

    private void UpdateSample()
    {
        var name = DateRulesViewModel.NameWithoutExtension(Sample);
        var usable = _lines.Where(line => line.Check is not null).Take(DateRule.MaxPatterns).ToList();
        if (name.Length == 0 || usable.Count == 0)
        {
            SampleKind = RuleSampleKind.Empty;
            SampleResult = name.Length == 0 ? _l.RuleSampleEmpty
                : IsRegex ? _l.RuleRegexSampleNoPatterns
                : _l.RuleSampleNoPatterns;
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
            SampleResult = IsRegex ? _l.RuleRegexSampleNoMatch : _l.RuleSampleNoMatch;
        }
    }

    private string Normalise(string text) =>
        string.Join('\n', text.Split('\n').Select(Clean).Where(line => line.Length > 0));

    /// <summary>
    /// One line as it is used: patterns are trimmed; a regular expression is kept as typed (a trailing <c>\ </c> or a
    /// space can matter), only the <c>\r</c> of a Windows line break is dropped. Lines of only white space are blank.
    /// </summary>
    private string Clean(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        return IsRegex ? line.TrimEnd('\r') : line.Trim();
    }

    /// <summary>One line of the pattern box: its 1-based number, trimmed text and either an error or a tester.</summary>
    private sealed record Line(int Number, string Text, DatePatternError? Error, Func<string, DateTime, PatternCheck>? Check);
}
