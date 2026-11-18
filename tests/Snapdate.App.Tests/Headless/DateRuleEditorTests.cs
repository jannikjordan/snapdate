using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Snapdate.App.Localization;
using Snapdate.App.Tests.Support;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Application.FileNames;
using Snapdate.Application.Ports;
using Snapdate.Domain.FileNames;

namespace Snapdate.App.Tests.Headless;

/// <summary>The rule editor in "Dates from file names": validation, tester, save/cancel and keyboard use.</summary>
public sealed class DateRuleEditorTests(HeadlessSession headless)
{
    private static readonly DateTime FileDate = new(2026, 7, 1, 10, 0, 0);

    private static readonly string[] LivePanels = ["ValidationPanel", "SampleResultPanel"];

    private static readonly DateRule MyScanner =
        new("custom-0a1b2c3d", "My scanner", RuleOrigin.Custom, Enabled: true, ["Scan_{YYYY}-{MM}-{DD}_*"]);

    private Task RunOnUi(Action action) =>
        headless.Session.Dispatch(action, TestContext.Current.CancellationToken);

    private Task RunOnUi(Func<Task> action) =>
        headless.Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            TestContext.Current.CancellationToken);

    private static LocalizedStrings Strings(string culture = "en-US") => new(CultureInfo.GetCultureInfo(culture));

    private static DateRulesViewModel Dialog(IDateRuleStore store, string culture = "en-US")
    {
        var dialog = new DateRulesViewModel(store, Strings(culture), TimeProvider.System);
        dialog.Open([]);
        return dialog;
    }

    private static DateRuleEditorViewModel NewRule(DateRulesViewModel dialog)
    {
        dialog.AddRuleCommand.Execute(null);
        return dialog.Editor!;
    }

    // ---------- Validation ----------

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Every_pattern_error_has_its_own_plain_message(string culture)
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore(), culture));
        var generic = editor.Describe(new DatePatternError(DatePatternErrorKind.InvalidExpression, 0));

        var messages = Enum.GetValues<DatePatternErrorKind>()
            .Select(kind => (Kind: kind, Text: editor.Describe(new DatePatternError(kind, 0, "M"))))
            .ToList();

        foreach (var (kind, text) in messages)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), kind.ToString());
            Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
            Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
            if (kind != DatePatternErrorKind.InvalidExpression)
            {
                Assert.NotEqual(generic, text);
            }
        }

        Assert.Equal(messages.Count, messages.Select(m => m.Text).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Every_regex_error_has_its_own_plain_message(string culture)
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore(), culture));
        var patternMessages = Enum.GetValues<DatePatternErrorKind>().Select(kind => editor.Describe(new DatePatternError(kind, 0, "M"))).ToList();
        editor.IsRegex = true;

        var messages = Enum.GetValues<DatePatternErrorKind>()
            .Select(kind => (Kind: kind, Text: editor.Describe(new DatePatternError(kind, 0, "M"))))
            .ToList();

        Assert.All(messages, m => Assert.DoesNotContain("{0}", m.Text, StringComparison.Ordinal));
        Assert.Equal(messages.Count, messages.Select(m => m.Text).Distinct(StringComparer.Ordinal).Count());

        // Regex mode talks about groups, not blocks.
        foreach (var kind in new[] { DatePatternErrorKind.MissingYear, DatePatternErrorKind.MissingMonth, DatePatternErrorKind.MissingDay, DatePatternErrorKind.HourWithoutMinute, DatePatternErrorKind.MinuteWithoutHour, DatePatternErrorKind.SecondWithoutMinute })
        {
            var text = messages.Single(m => m.Kind == kind).Text;
            Assert.Contains("(?<", text, StringComparison.Ordinal);
            Assert.DoesNotContain(text, patternMessages);
        }
    }

    [Theory]
    [InlineData(@"IMG-(?<year>\d{4})(?<day>\d{2})", "Line 1: The expression needs a group for the month: (?<month>…).")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?<counter>\d+)", "Line 1: The group “counter” is not a date part. Use year, yy, month, day, hour, minute or second, in lower case.")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2}", "Line 1: This is not a valid regular expression. Check the brackets and special characters around character 41.")]
    [InlineData(@"(?<=x)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})", "Line 1: Snapdate cannot run this expression safely. Leave out back-references such as \\1, look-arounds such as (?=…) or (?<=…), atomic groups (?>…) and conditions (?(…)…), and keep repetitions small.")]
    [InlineData(@"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2}).{1,500}", "Line 1: The repetition {1,500} is too large. At most 100 repetitions are possible.")]
    public void A_regular_expression_that_cannot_be_used_is_explained(string expression, string message)
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore()));
        editor.IsRegex = true;

        editor.PatternsText = expression;

        Assert.False(editor.IsValid);
        Assert.Equal([message], editor.Problems);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Switching_to_regular_expressions_keeps_the_text_and_checks_it_again()
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore()));
        editor.PatternsText = "IMG-{YYYY}{MM}{DD}-WA*";
        Assert.True(editor.IsValid);
        Assert.Equal("Patterns (one per line, up to 10)", editor.PatternsLabel);
        Assert.True(editor.IsPattern);

        editor.IsRegex = true;

        Assert.Equal(DateRuleSyntax.Regex, editor.Syntax);
        Assert.False(editor.IsPattern);
        Assert.Equal("IMG-{YYYY}{MM}{DD}-WA*", editor.PatternsText);
        Assert.Equal("Regular expressions (one per line, up to 10)", editor.PatternsLabel);
        Assert.Contains("named groups", editor.PatternsHint, StringComparison.Ordinal);
        Assert.Equal(@"^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$", editor.RegexExample);
        Assert.False(editor.IsValid);
        Assert.StartsWith("Line 1: ", editor.Problems[0], StringComparison.Ordinal);

        editor.PatternsText = editor.RegexExample;
        Assert.True(editor.IsValid);
        Assert.Equal("All regular expressions can be used.", editor.ValidationText);

        editor.Sample = "IMG-20240714-WA0001.jpg";
        Assert.Equal(RuleSampleKind.Match, editor.SampleKind);
        Assert.StartsWith("Matches line 1 → Sunday, July 14, 2024", editor.SampleResult, StringComparison.Ordinal);
        editor.Sample = "IMG_4711.jpg";
        Assert.Equal("No regular expression matches this name.", editor.SampleResult);
        editor.Sample = "IMG-٢٠٢٤٠٧١٤-WA0001.jpg";
        Assert.Equal(RuleSampleKind.NotADate, editor.SampleKind);

        editor.IsRegex = false;
        Assert.Equal(DateRuleSyntax.Pattern, editor.Syntax);
        Assert.Equal(editor.RegexExample, editor.PatternsText);
        Assert.Equal("Patterns (one per line, up to 10)", editor.PatternsLabel);
    }

    [Fact]
    public void A_new_regex_rule_starts_with_an_example_hint()
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore(), "de-DE"));

        editor.IsRegex = true;
        editor.Sample = "IMG-20240714-WA0001.jpg";

        Assert.Equal([@"Geben Sie mindestens einen regulären Ausdruck ein, zum Beispiel ^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$"], editor.Problems);
        Assert.Equal("Geben Sie zuerst einen gültigen regulären Ausdruck ein und probieren Sie dann einen Dateinamen aus.", editor.SampleResult);
        Assert.Equal(@"Beispiel: ^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$", editor.RegexExampleName);
    }

    [Fact]
    public void A_regex_rule_is_saved_as_regex_and_opens_in_regex_mode_again()
    {
        var store = new MemoryDateRuleStore();
        var dialog = Dialog(store);
        var editor = NewRule(dialog);
        editor.Name = "WhatsApp regex";
        editor.IsRegex = true;
        editor.PatternsText = @"^IMG-(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-WA\d+$";
        editor.SaveCommand.Execute(null);

        var saved = Assert.Single(store.User!.CustomRules);
        Assert.Equal(DateRuleSyntax.Regex, saved.Syntax);
        Assert.True(dialog.Items[0].CanEdit);
        dialog.TestName = "IMG-20160706-WA0002.jpg";
        Assert.Equal("Rule \"WhatsApp regex\": Wednesday, July 6, 2016", dialog.TestResult);

        dialog.Items[0].EditCommand.Execute(null);
        editor = dialog.Editor!;
        Assert.True(editor.IsRegex);
        Assert.False(editor.IsDirty);
        Assert.True(editor.IsValid);
        editor.SaveCommand.Execute(null);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Switching_an_existing_rule_to_regex_is_a_change_that_is_saved()
    {
        var store = new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner]));
        var dialog = Dialog(store);
        dialog.Items[0].EditCommand.Execute(null);
        var editor = dialog.Editor!;
        Assert.False(editor.IsRegex);

        editor.IsRegex = true;
        Assert.True(editor.IsDirty);
        editor.IsRegex = false;
        Assert.False(editor.IsDirty);

        editor.IsRegex = true;
        editor.PatternsText = @"Scan_(?<year>\d{4})-(?<month>\d\d)-(?<day>\d\d)_.*";
        editor.SaveCommand.Execute(null);

        var saved = Assert.Single(store.User!.CustomRules);
        Assert.Equal(MyScanner.Id, saved.Id);
        Assert.Equal(DateRuleSyntax.Regex, saved.Syntax);
        Assert.Equal([@"Scan_(?<year>\d{4})-(?<month>\d\d)-(?<day>\d\d)_.*"], saved.Patterns);
    }

    [Theory]
    [InlineData("IMG-{YYYY}{DD}", "Line 1: The pattern needs a month: {MM} or {M}.")]
    [InlineData("{YYYY}{MM}{DD", "Line 1: There is a “{” without a closing “}”.")]
    [InlineData("{YYYY}-{MM}-{DD}}", "Line 1: There is a “}” without an opening “{”. To use a brace as text, type it twice: }}")]
    [InlineData("{YYYY}{MM}{DD}{foo}", "Line 1: “{foo}” is not a block Snapdate knows. Use the blocks below, such as {YYYY}.")]
    [InlineData("{YYYY}{MM}{DD}{name}", "Line 1: “{name}” cannot be read from a file name. Use number blocks such as {MM} for the month.")]
    [InlineData("{YYYY}{YY}{MM}{DD}", "Line 1: “{YY}” repeats a part of the date that is already in the pattern. Each part may appear only once.")]
    [InlineData("{YYYY}{MM}{DD} {HH}", "Line 1: An hour also needs minutes: add {mm}.")]
    [InlineData("{YYYY}{M}{D}", "Line 1: The digits around “{D}” could be read in more than one way. Put a separator such as - or _ between the parts, or use two-digit blocks like {MM} and {DD}. Also, * must not touch these digits, and ? must not be used anywhere among them.")]
    [InlineData("{YYYY}?{MM}{D}", "Line 1: The digits around “{D}” could be read in more than one way. Put a separator such as - or _ between the parts, or use two-digit blocks like {MM} and {DD}. Also, * must not touch these digits, and ? must not be used anywhere among them.")]
    public void A_line_that_cannot_be_used_is_explained_and_save_is_disabled(string pattern, string message)
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore()));

        editor.PatternsText = pattern;

        Assert.False(editor.IsValid);
        Assert.Equal([message], editor.Problems);
        Assert.Equal(message, editor.ValidationText);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Problems_name_their_line_and_blank_lines_are_ignored()
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore(), "de-DE"));

        editor.PatternsText = "IMG-{YYYY}{MM}{DD}-WA*\r\n\r\n{YYYY}{MM}\r\n  ";

        Assert.Equal(["Zeile 3: Das Muster braucht einen Tag: {DD} oder {D}."], editor.Problems);

        editor.PatternsText = "IMG-{YYYY}{MM}{DD}-WA*\r\n\r\n  Scan_{YYYY}-{MM}-{DD}  \r\n";

        Assert.True(editor.IsValid);
        Assert.Equal("Alle Muster können verwendet werden.", editor.ValidationText);
        Assert.Equal(["IMG-{YYYY}{MM}{DD}-WA*", "Scan_{YYYY}-{MM}-{DD}"], editor.Patterns);
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void A_new_rule_starts_with_a_hint_and_at_most_ten_patterns_are_allowed()
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore()));

        Assert.Equal(["Type at least one pattern, for example IMG-{YYYY}{MM}{DD}-WA*"], editor.Problems);
        Assert.False(editor.SaveCommand.CanExecute(null));

        editor.PatternsText = string.Join("\n", Enumerable.Range(0, 12).Select(i => $"P{i}_{{YYYY}}{{MM}}{{DD}}"));

        Assert.Equal(["A rule can have at most 10 patterns. Please remove 2 line(s)."], editor.Problems);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Blocks_are_inserted_at_the_caret()
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore()));
        editor.PatternsText = "IMG--WA";
        editor.CaretIndex = 4;

        foreach (var label in new[] { "Year", "Month", "Day" })
        {
            editor.Chips.Single(c => c.Label == label).InsertCommand.Execute(null);
        }

        editor.CaretIndex = editor.PatternsText.Length;
        editor.Chips.Single(c => !c.IsToken).InsertCommand.Execute(null);

        Assert.Equal("IMG-{YYYY}{MM}{DD}-WA*", editor.PatternsText);
        Assert.Contains("? for exactly one character", editor.L.RulePatternsHint, StringComparison.Ordinal);
        Assert.Equal(["Year", "Month", "Day", "Hour", "Minute", "Second", "any text *"], editor.Chips.Select(c => c.Label));
        Assert.True(editor.IsValid);
    }

    // ---------- Tester ----------

    [Theory]
    [InlineData("en-US", "IMG_20240714_153208.jpg", RuleSampleKind.Match, "Matches line 2 → Sunday, July 14, 2024, 3:32:08")]
    [InlineData("de-DE", "IMG_20240714_153208.jpg", RuleSampleKind.Match, "Passt zu Zeile 2 → Sonntag, 14. Juli 2024, 15:32:08 Uhr")]
    [InlineData("en-US", "IMG_20241345_153208.jpg", RuleSampleKind.NotADate, "Line 2 matches, but 2024-13-45 15:32:08 is not a valid date.")]
    [InlineData("de-DE", "IMG_20241345_153208.jpg", RuleSampleKind.NotADate, "Zeile 2 passt, aber 2024-13-45 15:32:08 ist kein gültiges Datum.")]
    [InlineData("en-US", "IMG_18800101_120000.jpg", RuleSampleKind.NotADate, "Line 2 matches, but 1880-01-01 12:00:00 is before 1900 or in the future.")]
    [InlineData("en-US", "IMG_4711.jpg", RuleSampleKind.NoMatch, "No pattern matches this name.")]
    [InlineData("de-DE", "", RuleSampleKind.Empty, "Geben Sie einen Dateinamen ein, um Ihre Muster auszuprobieren.")]
    public void Tester_says_which_line_matches_and_why_a_fitting_name_gives_no_date(string culture, string sample, RuleSampleKind kind, string result)
    {
        var editor = NewRule(Dialog(new MemoryDateRuleStore(), culture));
        editor.PatternsText = "VID_{YYYY}{MM}{DD}*\nIMG_{YYYY}{MM}{DD}_{HH}{mm}{ss}";

        editor.Sample = sample;

        Assert.Equal(kind, editor.SampleKind);
        // The space before AM/PM differs between ICU versions.
        Assert.StartsWith(result, editor.SampleResult, StringComparison.Ordinal);
    }

    [Fact]
    public void Tester_starts_with_the_dialog_s_name_and_waits_for_a_valid_pattern()
    {
        var dialog = Dialog(new MemoryDateRuleStore());
        dialog.Open(["IMG_20240714_153208.jpg"]);
        dialog.TestName = "IMG_20240714_153208.jpg";

        var editor = NewRule(dialog);

        Assert.Equal("IMG_20240714_153208.jpg", editor.Sample);
        Assert.Equal(["IMG_20240714_153208.jpg"], editor.SampleNames);
        Assert.Equal(RuleSampleKind.Empty, editor.SampleKind);
        Assert.Equal("Type a valid pattern first, then try a file name.", editor.SampleResult);

        editor.PatternsText = "IMG_{YYYY}{MM}{DD}_*";
        Assert.Equal(RuleSampleKind.Match, editor.SampleKind);

        editor.SelectedSample = null;
        editor.Sample = "x";
        editor.SelectedSample = "IMG_20240714_153208.jpg";
        Assert.Equal("IMG_20240714_153208.jpg", editor.Sample);
    }

    // ---------- Save / cancel ----------

    [Theory]
    [InlineData("en-US", "", "My rule")]
    [InlineData("de-DE", "   ", "Meine Regel")]
    [InlineData("en-US", "  Scanner  ", "Scanner")]
    public void A_new_rule_is_saved_first_with_a_trimmed_or_default_name(string culture, string name, string saved)
    {
        var store = new MemoryDateRuleStore();
        var dialog = Dialog(store, culture);
        var editor = NewRule(dialog);
        Assert.True(editor.IsNew);
        Assert.False(dialog.IsListing);

        editor.Name = name;
        editor.PatternsText = "IMG_{YYYY}{MM}{DD}_*";
        editor.SaveCommand.Execute(null);

        Assert.Null(dialog.Editor);
        Assert.True(dialog.IsListing);
        Assert.Equal(1, store.Saves);
        var rule = Assert.Single(store.User!.CustomRules);
        Assert.Equal(saved, rule.Name);
        Assert.Equal(["IMG_{YYYY}{MM}{DD}_*"], rule.Patterns);
        Assert.True(rule.Enabled);
        Assert.Equal(rule.Id, dialog.Items[0].Id);
        Assert.Equal(rule.Id, dialog.FocusRuleId);
    }

    [Fact]
    public void Duplicate_names_are_allowed()
    {
        var store = new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner]));
        var dialog = Dialog(store);
        var editor = NewRule(dialog);

        editor.Name = "My scanner";
        editor.PatternsText = "Scan2_{YYYY}-{MM}-{DD}";
        editor.SaveCommand.Execute(null);

        Assert.Equal(["My scanner", "My scanner"], store.User!.CustomRules.Select(r => r.Name));
        Assert.Equal(2, store.User.CustomRules.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void A_new_rule_wins_over_the_built_in_rules_in_the_tester()
    {
        var dialog = Dialog(new MemoryDateRuleStore());
        dialog.TestName = "IMG-20160706-WA0002.jpg";
        Assert.Contains("WhatsApp (Android)", dialog.TestResult, StringComparison.Ordinal);

        var editor = NewRule(dialog);
        editor.Name = "Day first";
        editor.PatternsText = "IMG-{YYYY}{DD}{MM}-WA*";
        editor.SaveCommand.Execute(null);

        Assert.Equal("Rule \"Day first\": Tuesday, June 7, 2016", dialog.TestResult);
    }

    [Fact]
    public void Editing_a_duplicated_built_in_rule_keeps_its_own_id()
    {
        var store = new MemoryDateRuleStore();
        var dialog = Dialog(store);
        dialog.Items.Single(i => i.Id == "whatsapp-android").DuplicateCommand.Execute(null);
        var copy = dialog.Items[0];
        Assert.True(copy.CanEdit);

        copy.EditCommand.Execute(null);
        var editor = dialog.Editor!;
        Assert.False(editor.IsNew);
        Assert.Equal("Edit rule", editor.Title);
        Assert.Equal("WhatsApp (Android) (copy)", editor.Name);
        Assert.Equal("IMG-{YYYY}{MM}{DD}-WA*" + Environment.NewLine + "VID-{YYYY}{MM}{DD}-WA*", editor.PatternsText);

        editor.Name = "WhatsApp, my way";
        editor.PatternsText = "IMG-{YYYY}{MM}{DD}-WA*" + Environment.NewLine + "IMG-{YYYY}{MM}{DD}-WA* (?)";
        editor.SaveCommand.Execute(null);

        var saved = Assert.Single(store.User!.CustomRules);
        Assert.Equal(copy.Id, saved.Id);
        Assert.NotEqual("whatsapp-android", saved.Id);
        Assert.Equal("WhatsApp, my way", saved.Name);
        Assert.Equal(["IMG-{YYYY}{MM}{DD}-WA*", "IMG-{YYYY}{MM}{DD}-WA* (?)"], saved.Patterns);
        Assert.Equal(["WhatsApp, my way", "WhatsApp (Android)", "WhatsApp Desktop / Web"], dialog.Items.Select(i => i.Name));
        Assert.Equal(saved.Id, dialog.FocusRuleId);
    }

    [Fact]
    public void Built_in_rules_and_rules_in_an_unknown_syntax_cannot_be_edited()
    {
        var regex = new DateRule("custom-1234abcd", "Regex rule", RuleOrigin.Custom, Enabled: true, ["(?<year>\\d{4})"], "regex-v9");
        var dialog = Dialog(new MemoryDateRuleStore(userRules: new UserDateRules([], [regex])));

        Assert.False(dialog.Items[0].CanEdit);
        dialog.Items[0].EditCommand.Execute(null);
        Assert.Null(dialog.Editor);
        Assert.All(dialog.Items.Where(i => i.IsSystem), i => Assert.False(i.CanEdit));
    }

    [Fact]
    public void Cancel_without_changes_closes_and_with_changes_asks_first()
    {
        var store = new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner]));
        var dialog = Dialog(store);
        dialog.Items[0].EditCommand.Execute(null);
        var editor = dialog.Editor!;
        Assert.False(editor.IsDirty);

        editor.CancelCommand.Execute(null);
        Assert.Null(dialog.Editor);
        Assert.Equal(MyScanner.Id, dialog.FocusRuleId);

        dialog.Items[0].EditCommand.Execute(null);
        editor = dialog.Editor!;
        editor.PatternsText += "\n";
        Assert.False(editor.IsDirty, "A blank line is no change.");
        editor.Name = "Other";
        Assert.True(editor.IsDirty);

        // Esc asks; Esc again keeps editing; Cancel asks; Discard leaves without saving.
        dialog.Escape();
        Assert.True(editor.IsConfirmingDiscard);
        Assert.False(editor.ShowButtons);
        dialog.Escape();
        Assert.False(editor.IsConfirmingDiscard);
        Assert.Same(editor, dialog.Editor);
        editor.CancelCommand.Execute(null);
        editor.KeepEditingCommand.Execute(null);
        Assert.Same(editor, dialog.Editor);
        editor.CancelCommand.Execute(null);
        editor.DiscardCommand.Execute(null);

        Assert.Null(dialog.Editor);
        Assert.True(dialog.IsOpen);
        Assert.Equal(0, store.Saves);
        Assert.Equal("My scanner", dialog.Items[0].Name);
    }

    [Fact]
    public void Closing_and_reopening_the_dialog_leaves_the_editor()
    {
        var dialog = Dialog(new MemoryDateRuleStore());
        NewRule(dialog).PatternsText = "x";

        dialog.CloseCommand.Execute(null);
        Assert.Null(dialog.Editor);
        dialog.Open([]);
        Assert.Null(dialog.Editor);
        Assert.True(dialog.IsListing);
    }

    [Fact]
    public void A_refused_save_keeps_the_editor_open_and_the_next_save_works()
    {
        var store = new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner]));
        var dialog = Dialog(store);
        dialog.Items[0].EditCommand.Execute(null);
        var editor = dialog.Editor!;
        editor.Name = "Scanner 2";
        // Another window switches a built-in rule off meanwhile.
        store.Write(new UserDateRules(["whatsapp-desktop"], [MyScanner]));

        editor.SaveCommand.Execute(null);

        Assert.Same(editor, dialog.Editor);
        Assert.Equal(Strings().RulesConflict, dialog.MessageText);
        Assert.Equal(0, store.Saves);
        Assert.Equal("Scanner 2", editor.Name);

        editor.SaveCommand.Execute(null);

        Assert.Null(dialog.Editor);
        Assert.Equal(1, store.Saves);
        Assert.Equal("Scanner 2", store.User!.CustomRules.Single().Name);
        Assert.Equal(["whatsapp-desktop"], store.User.DisabledSystemRuleIds);
    }

    [Fact]
    public void A_rule_deleted_elsewhere_while_editing_is_saved_as_a_new_rule()
    {
        var store = new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner]));
        var dialog = Dialog(store);
        dialog.Items[0].EditCommand.Execute(null);
        var editor = dialog.Editor!;
        editor.Name = "My scanner 2";
        store.Write(UserDateRules.Empty);

        editor.SaveCommand.Execute(null); // conflict: reloaded without the rule
        editor.SaveCommand.Execute(null);

        var saved = Assert.Single(store.User!.CustomRules);
        Assert.Equal("My scanner 2", saved.Name);
        Assert.Null(dialog.Editor);
    }

    [Fact]
    public void Read_only_rules_offer_no_editor()
    {
        var dialog = new DateRulesViewModel(new ReadOnlyStore(), Strings(), TimeProvider.System);

        Assert.False(dialog.CanAdd);
        dialog.AddRuleCommand.Execute(null);
        dialog.TestName = "IMG_20240714_153208.jpg";

        Assert.Null(dialog.Editor);
        Assert.Equal(Strings().RulesReadOnly, dialog.MessageText);
        Assert.False(dialog.CanCreateFromName);
    }

    // ---------- Create from this file name ----------

    [Fact]
    public void A_name_no_rule_reads_offers_a_rule_made_from_it()
    {
        var store = new MemoryDateRuleStore();
        var dialog = Dialog(store);

        dialog.TestName = "IMG_4711.jpg";
        Assert.False(dialog.CanCreateFromName);
        dialog.TestName = "IMG-20240714-WA0001.jpg";
        Assert.False(dialog.CanCreateFromName, "A rule already reads it.");

        dialog.TestName = "IMG_20240714_153208.jpg";
        Assert.True(dialog.CanCreateFromName);
        dialog.CreateFromNameCommand.Execute(null);

        var editor = dialog.Editor!;
        Assert.Equal("IMG_{YYYY}{MM}{DD}*", editor.PatternsText);
        Assert.False(editor.IsDirty);
        Assert.Equal(RuleSampleKind.Match, editor.SampleKind);
        Assert.Equal("Matches line 1 → Sunday, July 14, 2024", editor.SampleResult);

        editor.SaveCommand.Execute(null);
        Assert.Equal("My rule", store.User!.CustomRules.Single().Name);
        Assert.Equal(RuleTestKind.Match, dialog.TestKind);
        Assert.False(dialog.CanCreateFromName);
    }

    // ---------- Scan ----------

    [Fact]
    public Task A_new_rule_takes_effect_on_the_next_scan() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg");
        var vm = new TestServices { DateRules = new MemoryDateRuleStore() }.CreateViewModel();
        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;
        Assert.Equal("WhatsApp (Android)", vm.VisibleItems.Single().Item.Photo.ResolvedDate.RuleName);

        vm.OpenDateRulesCommand.Execute(null);
        var editor = NewRule(vm.RulesDialog!);
        editor.Name = "Day first";
        editor.PatternsText = "IMG-{YYYY}{DD}{MM}-WA*";
        editor.SaveCommand.Execute(null);
        vm.RulesDialog!.CloseCommand.Execute(null);
        await vm.ScanCompletion;

        var photo = vm.VisibleItems.Single().Item.Photo;
        Assert.Equal("Day first", photo.ResolvedDate.RuleName);
        Assert.Equal(new DateTime(2016, 6, 7), photo.ResolvedDate.Date.Value.Date);
    });

    [Fact]
    public Task Saving_an_unchanged_rule_only_closes_the_editor_and_does_not_rescan() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/Scan_2023-02-01_7.jpg");
        var store = new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner]));
        var vm = new TestServices { DateRules = store }.CreateViewModel();
        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;
        var scans = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Phase) && vm.Phase == Phase.Scanning)
            {
                scans++;
            }
        };

        vm.OpenDateRulesCommand.Execute(null);
        var dialog = vm.RulesDialog!;
        dialog.Items[0].EditCommand.Execute(null);
        var editor = dialog.Editor!;
        // Only white space differs: the saved rule would be the same.
        editor.PatternsText = "  " + editor.PatternsText + "\n";
        editor.SaveCommand.Execute(null);

        Assert.Null(dialog.Editor);
        Assert.Equal(MyScanner.Id, dialog.FocusRuleId);
        Assert.Equal(0, store.Saves);
        dialog.CloseCommand.Execute(null);
        await vm.ScanCompletion;
        Assert.Equal(0, scans);
    });

    // ---------- Window ----------

    [Fact]
    public Task Editor_works_with_the_keyboard_alone() => RunOnUi(() =>
    {
        var store = new MemoryDateRuleStore();
        var vm = new TestServices { DateRules = store }.CreateViewModel();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();
        Settle(window);
        window.FindControl<Button>("DateRulesLink")!.Focus(NavigationMethod.Tab);
        Press(window, PhysicalKey.Enter);
        var dialogView = window.FindControl<DateRulesDialog>("DateRulesDialog")!;
        var root = DialogRoot(window);

        // "Add rule" opens the editor; the name box has the keyboard.
        dialogView.FindControl<Button>("AddRuleButton")!.Focus(NavigationMethod.Tab);
        Press(window, PhysicalKey.Enter);
        Assert.NotNull(vm.RulesDialog!.Editor);
        Assert.Equal("RuleNameBox", Focused(window).Name);
        Assert.False(dialogView.FindControl<ScrollViewer>("RuleScroller")!.IsEffectivelyVisible);
        Assert.False(dialogView.FindControl<Button>("CloseButton")!.IsEffectivelyVisible);
        window.KeyTextInput("Phone");
        Settle(window);

        // Tab to the pattern box and type; Enter there starts a new line.
        Press(window, PhysicalKey.Tab);
        Assert.Equal("PatternsBox", Focused(window).Name);
        window.KeyTextInput("IMG_{YYYY}{MM}{DD}_*");
        Press(window, PhysicalKey.Enter);
        window.KeyTextInput("{YYYY}{MM}");
        Settle(window);
        Assert.Equal(["Line 2: The pattern needs a day: {DD} or {D}."], vm.RulesDialog.Editor!.Problems);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);
        Assert.True(vm.RulesDialog.Editor.IsValid);

        // Tab goes top to bottom through blocks, tester and buttons, and stays inside the dialog.
        var order = new List<Control>();
        for (var i = 0; i < 14; i++)
        {
            Press(window, PhysicalKey.Tab);
            var focused = Focused(window);
            Assert.True(root.IsVisualAncestorOf(focused), $"Focus left the dialog: {focused}");
            order.Add(focused);
            if (focused.Name == "SaveRuleButton")
            {
                break;
            }
        }

        Assert.Contains(order, c => c.Name == "SampleBox" || c.FindAncestorOfType<TextBox>()?.Name == "SampleBox");
        Assert.Equal("EditorCancelButton", order[^2].Name);
        Assert.Equal("SaveRuleButton", order[^1].Name);
        var tops = order.Take(order.Count - 2).Select(c => c.TranslatePoint(default, root)!.Value.Y).ToList();
        Assert.Equal(tops.Order(), tops);

        // Enter saves; the new rule's switch gets the keyboard.
        Press(window, PhysicalKey.Enter);
        Assert.Null(vm.RulesDialog.Editor);
        Assert.Equal("Phone on/off", AutomationProperties.GetName(Focused(window)));
        Assert.Equal(["IMG_{YYYY}{MM}{DD}_*"], store.User!.CustomRules.Single().Patterns);

        // Edit via keyboard, change something, Esc asks (focus on Keep editing), Shift+Tab + Enter discards.
        Press(window, PhysicalKey.Tab);
        Assert.Equal("Edit Phone", AutomationProperties.GetName(Focused(window)));
        Press(window, PhysicalKey.Enter);
        Assert.Equal("RuleNameBox", Focused(window).Name);
        window.KeyTextInput("!");
        Press(window, PhysicalKey.Escape);
        Assert.True(vm.RulesDialog.Editor!.IsConfirmingDiscard);
        Assert.Equal("KeepEditingButton", Focused(window).Name);
        Press(window, PhysicalKey.Escape);
        Assert.Equal("EditorCancelButton", Focused(window).Name);
        Press(window, PhysicalKey.Escape);
        Assert.Equal("KeepEditingButton", Focused(window).Name);
        Press(window, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal("DiscardButton", Focused(window).Name);
        Press(window, PhysicalKey.Enter);
        Assert.Null(vm.RulesDialog.Editor);
        Assert.True(vm.RulesDialog.IsOpen);
        Assert.Equal("Phone on/off", AutomationProperties.GetName(Focused(window)));
        Assert.Equal("Phone", store.User!.CustomRules.Single().Name);
        window.Close();
    });

    [Fact]
    public Task Validation_and_tester_results_are_live_regions() => RunOnUi(() =>
    {
        var vm = new TestServices().CreateViewModel();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();
        vm.OpenDateRulesCommand.Execute(null);
        vm.RulesDialog!.AddRuleCommand.Execute(null);
        vm.RulesDialog.Editor!.PatternsText = "{YYYY}{MM}";
        Settle(window);
        var editor = window.FindControl<DateRulesDialog>("DateRulesDialog")!.FindControl<DateRuleEditor>("RuleEditor")!;

        foreach (var name in LivePanels)
        {
            var panel = editor.FindControl<Control>(name)!;
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(panel));
        }

        Assert.Equal("Line 1: The pattern needs a day: {DD} or {D}.", AutomationProperties.GetName(editor.FindControl<Control>("ValidationPanel")!));
        Assert.Equal("Patterns (one per line, up to 10)", AutomationProperties.GetName(editor.FindControl<Control>("PatternsBox")!));
        window.Close();
    });

    [Fact]
    public Task The_regex_switch_replaces_the_blocks_with_help_and_an_example() => RunOnUi(() =>
    {
        var vm = new TestServices().CreateViewModel();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();
        vm.OpenDateRulesCommand.Execute(null);
        vm.RulesDialog!.AddRuleCommand.Execute(null);
        Settle(window);
        var view = window.FindControl<DateRulesDialog>("DateRulesDialog")!.FindControl<DateRuleEditor>("RuleEditor")!;
        var toggle = view.FindControl<CheckBox>("RegexToggle")!;
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.Equal("Advanced: regular expressions", AutomationProperties.GetName(toggle));
        Assert.True(view.FindControl<Control>("ChipList")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<Control>("RegexExampleText")!.IsEffectivelyVisible);

        toggle.Focus();
        Press(window, PhysicalKey.Space);

        Assert.True(vm.RulesDialog.Editor!.IsRegex);
        Assert.False(view.FindControl<Control>("ChipList")!.IsEffectivelyVisible);
        Assert.True(view.FindControl<Control>("RegexExampleText")!.IsEffectivelyVisible);
        Assert.Equal("Regular expressions (one per line, up to 10)", AutomationProperties.GetName(view.FindControl<Control>("PatternsBox")!));
        Assert.StartsWith("Mark the date parts", AutomationProperties.GetHelpText(view.FindControl<Control>("PatternsBox")!), StringComparison.Ordinal);
        window.Close();
    });

    [Theory]
    [InlineData("en-US", false)]
    [InlineData("de-DE", false)]
    [InlineData("en-US", true)]
    [InlineData("de-DE", true)]
    public Task Editor_fits_the_smallest_window(string culture, bool regex) => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG_20240714_153208.jpg");
        var vm = new TestServices { DateRules = new MemoryDateRuleStore() }.CreateViewModel(culture);
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();
        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;
        vm.OpenDateRulesCommand.Execute(null);
        var dialog = vm.RulesDialog!;
        dialog.SelectedSample = dialog.SampleNames[0];
        dialog.AddRuleCommand.Execute(null);
        // A tall state: several problems, a long name, the tester explaining a wrong date.
        var editor = dialog.Editor!;
        editor.Name = new string('W', UserDateRules.MaxNameLength);
        editor.IsRegex = regex;
        editor.PatternsText = regex
            ? @"IMG_(?<year>\d{4})(?<day>\d\d)(?<month>\d\d)_.*" + "\n(?=x)(?<year>1)(?<month>1)(?<day>1)\n(?<year>1)(?<month>1)(?<day>1)(?<tag>x)\n(?<year>1)(?<month>1)(?<day>1)x{500}\n(?<year>1)(?<month>1)(?<day>1)(" + new string('x', 150)
            : "IMG_{YYYY}{DD}{MM}_*\n{YYYY}{M}{D}\n{YYYY}{MM}{DD}{name}\n{YYYY}-{MM}\n" + new string('x', 150) + "{YYYY}{MM}{DD}";
        editor.CancelCommand.Execute(null);
        Settle(window);

        Assert.True(editor.IsConfirmingDiscard);
        Assert.Equal(regex ? 4 : 3, editor.Problems.Count);
        Assert.Equal(RuleSampleKind.NotADate, editor.SampleKind);
        var root = DialogRoot(window);
        var rootTopLeft = root.TranslatePoint(default, window)!.Value;
        Assert.True(rootTopLeft.Y >= 0 && rootTopLeft.Y + root.Bounds.Height <= 720.5, $"Dialog is {root.Bounds.Height:0} px tall.");

        var view = window.FindControl<DateRulesDialog>("DateRulesDialog")!.FindControl<DateRuleEditor>("RuleEditor")!;
        var scroller = view.FindControl<ScrollViewer>("EditorScroller")!;
        foreach (var control in view.GetVisualDescendants().OfType<Control>()
                     .Where(c => c is TextBlock or Button or TextBox or ComboBox && c.IsEffectivelyVisible))
        {
            var topLeft = control.TranslatePoint(default, root)!.Value;
            if (control.FindAncestorOfType<TextBox>() is null)
            {
                Assert.True(topLeft.X + control.Bounds.Width <= root.Bounds.Width + 0.5, $"{control} sticks out ({culture}).");
            }

            if (!scroller.IsVisualAncestorOf(control))
            {
                Assert.True(topLeft.Y + control.Bounds.Height <= root.Bounds.Height + 0.5, $"{control} is cut off ({culture}).");
            }
        }

        // Buttons and short texts are never shortened.
        foreach (var text in view.GetVisualDescendants().OfType<TextBlock>()
                     .Where(t => t.IsEffectivelyVisible && t.FindAncestorOfType<TextBox>() is null
                         && t.TextTrimming == Avalonia.Media.TextTrimming.None && t.TextWrapping == Avalonia.Media.TextWrapping.NoWrap))
        {
            text.Measure(Size.Infinity);
            var needed = text.DesiredSize.Width - text.Margin.Left - text.Margin.Right;
            Assert.True(needed <= text.Bounds.Width + 0.5, $"\"{text.Text}\" is clipped ({culture}).");
        }

        window.Close();
    });

    private static void Jpeg(TempFolder temp, string relative)
    {
        var path = temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
        File.SetCreationTime(path, FileDate);
        File.SetLastWriteTime(path, FileDate);
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Settle(window);
    }

    private static Control Focused(Window window) => (Control)window.FocusManager!.GetFocusedElement()!;

    private static Border DialogRoot(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DialogRoot");

    /// <summary>User rules written by a newer version: read-only.</summary>
    private sealed class ReadOnlyStore : IDateRuleStore
    {
        public IReadOnlyList<DateRule> LoadSystemRules() => new MemoryDateRuleStore().LoadSystemRules();

        public UserDateRulesLoad LoadUserRules() =>
            new(UserDateRules.Empty, UserRulesStatus.NewerVersion, UserRulesFingerprint.ForContent(3, "ABC"));

        public UserDateRulesSave SaveUserRules(UserDateRules rules, UserRulesFingerprint expected) =>
            new(DateRuleSaveResult.ReadOnly, expected);
    }
}
