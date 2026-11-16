using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snapdate.App.Localization;
using Snapdate.Domain.Naming;
using Snapdate.Domain.Photos;

namespace Snapdate.App.ViewModels;

/// <summary>
/// The file name builder: a free-text pattern box, clickable blocks ("chips") that insert
/// placeholders at the caret, a pill view of the parsed pattern and quick presets.
/// </summary>
public sealed partial class PatternBuilderViewModel : ObservableObject
{
    /// <summary>Sample photo used for pill previews and the example when no photos are loaded.</summary>
    public static readonly PhotoFacts SampleFacts = new(
        "IMG_4711", ".jpg", new CaptureDate(new DateTime(2024, 7, 4, 9, 5, 8), DateSource.ExifOriginal), "iPhone 15 Pro", 1);

    private static readonly string[] PresetPatterns =
    [
        "{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}",
        "{YYYY}-{MM}-{DD} {HH}{mm}",
        "{YYYY}{MM}{DD}_{HH}{mm}{ss}",
        "{YYYY}/{MM}/{YYYY}-{MM}-{DD} {name}",
        "{DD}.{MM}.{YYYY} {name}",
    ];

    private LocalizedStrings _l;
    private bool _suppressZeroConversion;

    public PatternBuilderViewModel(LocalizedStrings strings, string initialText)
    {
        _l = strings;
        Text = initialText;
        LeadingZeros = !ContainsNoZeroTokens(Parsed);
        RebuildChips();
        RebuildPresets();
    }

    /// <summary>Raised whenever the effective pattern changes (valid or not).</summary>
    public event EventHandler? PatternChanged;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Caret position in the text box, so blocks are inserted where the user is typing.</summary>
    [ObservableProperty]
    public partial int CaretIndex { get; set; }

    [ObservableProperty]
    public partial bool LeadingZeros { get; set; } = true;

    [ObservableProperty]
    public partial NamePattern? Pattern { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    public ObservableCollection<PatternPillViewModel> Pills { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ChipGroupViewModel> ChipGroups { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PresetViewModel> Presets { get; private set; } = [];

    private PatternParseResult Parsed => NamePattern.Parse(Text);

    public void UpdateStrings(LocalizedStrings strings)
    {
        _l = strings;
        RebuildChips();
        RebuildPresets();
        Reparse();
    }

    [RelayCommand]
    private void Reset()
    {
        Text = NamePattern.DefaultText;
        CaretIndex = Text.Length;
    }

    /// <summary>Inserts text (a placeholder or separator) at the caret.</summary>
    public void Insert(string snippet)
    {
        var caret = Math.Clamp(CaretIndex, 0, Text.Length);
        Text = Text.Insert(caret, snippet);
        CaretIndex = caret + snippet.Length;
    }

    partial void OnTextChanged(string value) => Reparse();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnLeadingZerosChanged(bool value)
    {
        RebuildChips();
        if (_suppressZeroConversion || Pattern is null)
        {
            return;
        }

        // Switch every date/time block in the current pattern to the chosen style.
        var converted = Pattern.Segments.Select(s => s is PatternSegment.Token t
                && TokenCatalog.LeadingZeroCounterpart(t.Kind) is { } other
                && TokenCatalog.HasLeadingZero(t.Kind) != value
            ? new PatternSegment.Token(other)
            : s);
        var result = NamePattern.FromSegments(converted);
        if (result.Pattern is not null)
        {
            Text = result.Pattern.Text;
        }
    }

    private void Reparse()
    {
        var result = NamePattern.Parse(Text);
        Pattern = result.Pattern;
        ErrorMessage = result.IsValid ? null : Describe(result.Errors[0]);

        Pills.Clear();
        if (result.Pattern is { } pattern)
        {
            for (var i = 0; i < pattern.Segments.Count; i++)
            {
                Pills.Add(CreatePill(pattern, i));
            }

            // Keep the toggle in sync when the user types {M} etc. by hand.
            _suppressZeroConversion = true;
            if (pattern.Segments.Any(s => s is PatternSegment.Token t && TokenCatalog.LeadingZeroCounterpart(t.Kind) is not null))
            {
                LeadingZeros = !ContainsNoZeroTokens(result);
            }

            _suppressZeroConversion = false;
        }

        PatternChanged?.Invoke(this, EventArgs.Empty);
    }

    private PatternPillViewModel CreatePill(NamePattern pattern, int index)
    {
        var segment = pattern.Segments[index];
        void Replace(PatternSegment? replacement)
        {
            var list = pattern.Segments.ToList();
            if (replacement is null)
            {
                list.RemoveAt(index);
            }
            else
            {
                list[index] = replacement;
            }

            var rebuilt = NamePattern.FromSegments(list);
            Text = rebuilt.Pattern?.Text ?? string.Empty;
            CaretIndex = Text.Length;
        }

        if (segment is PatternSegment.Token token)
        {
            // Clicking a block switches to its other form: 07 ⇄ 7, 2024 ⇄ 24, Jul ⇄ July, Sun ⇄ Sunday.
            var counterpart = TokenCatalog.Alternate(token.Kind);
            return new PatternPillViewModel(
                IsToken: true,
                Label: TokenLabel(token.Kind),
                Value: SampleValue(token.Kind),
                Tooltip: counterpart is { } other
                    ? _l.Format(nameof(LocalizedStrings.PillToggle), SampleValue(other))
                    : "{" + TokenCatalog.NameOf(token.Kind) + "}",
                CanToggle: counterpart is not null,
                ToggleCommand: new RelayCommand(() => Replace(new PatternSegment.Token(counterpart ?? token.Kind))),
                RemoveCommand: new RelayCommand(() => Replace(null)),
                RemoveLabel: _l.PillRemove);
        }

        var text = ((PatternSegment.Literal)segment).Text;
        return new PatternPillViewModel(
            IsToken: false,
            Label: string.Empty,
            Value: text.Replace(' ', '␣').Replace("/", " / ", StringComparison.Ordinal),
            Tooltip: text == "/" ? _l.SepFolder : text,
            CanToggle: false,
            ToggleCommand: new RelayCommand(() => { }),
            RemoveCommand: new RelayCommand(() => Replace(null)),
            RemoveLabel: _l.PillRemove);
    }

    private void RebuildChips()
    {
        var z = LeadingZeros;
        ChipGroups =
        [
            new ChipGroupViewModel(_l.GroupDate,
            [
                // Long/short variants are reached by clicking the inserted block.
                Chip(TokenKind.Year4),
                Chip(z ? TokenKind.Month2 : TokenKind.Month1),
                Chip(z ? TokenKind.Day2 : TokenKind.Day1),
                Chip(TokenKind.MonthNameShort),
                Chip(TokenKind.WeekdayShort),
            ]),
            new ChipGroupViewModel(_l.GroupTime,
            [
                Chip(z ? TokenKind.Hour2 : TokenKind.Hour1),
                Chip(z ? TokenKind.Minute2 : TokenKind.Minute1),
                Chip(z ? TokenKind.Second2 : TokenKind.Second1),
            ]),
            new ChipGroupViewModel(_l.GroupFile,
            [
                Chip(TokenKind.OriginalName),
                Chip(TokenKind.Camera),
                Chip(TokenKind.Counter),
            ]),
            new ChipGroupViewModel(_l.GroupSeparator,
            [
                Separator("-", "-"),
                Separator("_", "_"),
                Separator(".", "."),
                Separator(" ", "␣ " + _l.SepSpace),
                Separator("/", "/ " + _l.SepFolder),
            ]),
        ];
    }

    private void RebuildPresets()
    {
        Presets = PresetPatterns.Select(p =>
        {
            var pattern = NamePattern.Parse(p).Pattern!;
            var label = pattern.Render(SampleFacts, _l.Culture).Replace('/', '\\');
            return new PresetViewModel(label, p, new RelayCommand(() =>
            {
                Text = p;
                CaretIndex = Text.Length;
            }));
        }).ToList();
    }

    private ChipViewModel Chip(TokenKind kind)
    {
        var raw = "{" + TokenCatalog.NameOf(kind) + "}";
        return new ChipViewModel(TokenLabel(kind), $"{raw}  →  {SampleValue(kind)}", new RelayCommand(() => Insert(raw)), IsToken: true);
    }

    private ChipViewModel Separator(string text, string label) =>
        new(label, text == "/" ? _l.SepFolder : $"\"{text}\"", new RelayCommand(() => Insert(text)), IsToken: false);

    private string SampleValue(TokenKind kind) =>
        NamePattern.FromSegments([new PatternSegment.Token(kind)]).Pattern!.Render(SampleFacts, _l.Culture);

    private string TokenLabel(TokenKind kind) => kind switch
    {
        TokenKind.Year4 => _l.TokYear,
        TokenKind.Year2 => _l.TokYearShort,
        TokenKind.Month2 or TokenKind.Month1 => _l.TokMonth,
        TokenKind.MonthNameShort => _l.TokMonthName,
        TokenKind.MonthNameLong => _l.TokMonthNameLong,
        TokenKind.Day2 or TokenKind.Day1 => _l.TokDay,
        TokenKind.WeekdayShort => _l.TokWeekday,
        TokenKind.WeekdayLong => _l.TokWeekdayLong,
        TokenKind.Hour2 or TokenKind.Hour1 => _l.TokHour,
        TokenKind.Minute2 or TokenKind.Minute1 => _l.TokMinute,
        TokenKind.Second2 or TokenKind.Second1 => _l.TokSecond,
        TokenKind.OriginalName => _l.TokName,
        TokenKind.Camera => _l.TokCamera,
        TokenKind.Counter => _l.TokCounter,
        _ => kind.ToString(),
    };

    private string Describe(PatternError error) => error.Kind switch
    {
        PatternErrorKind.Empty => _l.Format(nameof(LocalizedStrings.ErrEmpty)),
        PatternErrorKind.UnknownPlaceholder => _l.Format(nameof(LocalizedStrings.ErrUnknownPlaceholder), error.Detail),
        PatternErrorKind.UnclosedBrace => _l.Format(nameof(LocalizedStrings.ErrUnclosedBrace)),
        PatternErrorKind.UnexpectedClosingBrace => _l.Format(nameof(LocalizedStrings.ErrUnexpectedClosingBrace)),
        PatternErrorKind.InvalidCharacter => _l.Format(nameof(LocalizedStrings.ErrInvalidCharacter), error.Detail),
        PatternErrorKind.EmptyFolderName => _l.Format(nameof(LocalizedStrings.ErrEmptyFolderName)),
        _ => error.Kind.ToString(),
    };

    private static bool ContainsNoZeroTokens(PatternParseResult result) =>
        result.Pattern?.Segments.Any(s => s is PatternSegment.Token t
            && TokenCatalog.LeadingZeroCounterpart(t.Kind) is not null
            && !TokenCatalog.HasLeadingZero(t.Kind)) ?? false;
}

/// <summary>One block in the parsed pattern view.</summary>
public sealed record PatternPillViewModel(
    bool IsToken,
    string Label,
    string Value,
    string Tooltip,
    bool CanToggle,
    IRelayCommand ToggleCommand,
    IRelayCommand RemoveCommand,
    string RemoveLabel);

/// <summary>A clickable block that inserts a placeholder or separator.</summary>
public sealed record ChipViewModel(string Label, string Tooltip, IRelayCommand InsertCommand, bool IsToken);

public sealed record ChipGroupViewModel(string Title, IReadOnlyList<ChipViewModel> Chips);

public sealed record PresetViewModel(string Label, string Pattern, IRelayCommand ApplyCommand);
