using System.Globalization;
using Avalonia;
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
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.App.Tests.Headless;

/// <summary>"Dates from file names": list, switches, duplicate, delete, tester, read-only states and keyboard use.</summary>
public sealed class DateRulesDialogTests(HeadlessSession headless)
{
    private static readonly DateTime FileDate = new(2026, 7, 1, 10, 0, 0);

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

    private static DateRulesViewModel Dialog(IDateRuleStore store, string culture = "en-US") =>
        new(store, Strings(culture), TimeProvider.System);

    private static void Jpeg(TempFolder temp, string relative, DateTime? taken = null, string? camera = null)
    {
        var path = temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, taken is null ? [0xFF, 0xD8, 0xFF, 0xD9] : TestImages.JpegWithExif(taken, camera));
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

    // ---------- List ----------

    [Theory]
    [InlineData("en-US", "Yours", "Built-in", "WhatsApp (Android) on/off")]
    [InlineData("de-DE", "Eigene", "Mitgeliefert", "WhatsApp (Android) ein/aus")]
    public void Lists_own_rules_first_with_source_badge_and_switch_names(string culture, string yours, string builtIn, string toggle)
    {
        var dialog = Dialog(new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner])), culture);

        Assert.Equal(["My scanner", "WhatsApp (Android)", "WhatsApp Desktop / Web"], dialog.Items.Select(i => i.Name));
        Assert.Equal(yours, dialog.Items[0].SourceText);
        Assert.True(dialog.Items[0].CanDelete);
        Assert.False(dialog.Items[0].CanDuplicate);
        Assert.Equal(builtIn, dialog.Items[1].SourceText);
        Assert.True(dialog.Items[1].CanDuplicate);
        Assert.False(dialog.Items[1].CanDelete);
        Assert.Equal(toggle, dialog.Items[1].ToggleName);
        Assert.All(dialog.Items, i => Assert.True(i.IsEnabled));
        Assert.True(dialog.Items[0].CanEdit);
        Assert.All(dialog.Items.Skip(1), i => Assert.False(i.CanEdit));
        Assert.True(dialog.CanAdd);
        Assert.Contains("IMG-{YYYY}{MM}{DD}-WA*", dialog.Items[1].PatternsText, StringComparison.Ordinal);
        Assert.False(dialog.HasStatus);
    }

    [Fact]
    public void Rules_in_an_unknown_syntax_are_listed_with_their_syntax()
    {
        var regex = new DateRule("custom-1234abcd", "Regex rule", RuleOrigin.Custom, Enabled: true, ["(?<year>\\d{4})"], "regex-v9");

        var item = Dialog(new MemoryDateRuleStore(userRules: new UserDateRules([], [regex]))).Items[0];

        Assert.True(item.IsSyntaxUnsupported);
        Assert.Equal("regex-v9", item.SyntaxLabel);
        Assert.Contains("\"regex-v9\"", item.SyntaxTip, StringComparison.Ordinal);
    }

    // ---------- Tester ----------

    [Fact]
    public void Tester_names_the_rule_and_the_date_with_time_in_the_ui_culture()
    {
        var dialog = Dialog(new MemoryDateRuleStore());
        Assert.Equal(RuleTestKind.Empty, dialog.TestKind);

        dialog.TestName = "WhatsApp Image 2024-07-14 at 15.32.08.jpeg";

        Assert.Equal(RuleTestKind.Match, dialog.TestKind);
        Assert.StartsWith("Rule \"WhatsApp Desktop / Web\": Sunday, July 14, 2024, 3:32:08", dialog.TestResult, StringComparison.Ordinal);
    }

    [Fact]
    public void Tester_in_German_formats_a_date_only_name_without_time()
    {
        var dialog = Dialog(new MemoryDateRuleStore(), "de-DE");

        dialog.TestName = "IMG-20240714-WA0001.jpg";

        Assert.Equal("Regel „WhatsApp (Android)“: Sonntag, 14. Juli 2024", dialog.TestResult);

        dialog.TestName = "WhatsApp Image 2024-07-14 at 15.32.08";
        Assert.Equal("Regel „WhatsApp Desktop / Web“: Sonntag, 14. Juli 2024, 15:32:08 Uhr", dialog.TestResult);
    }

    [Fact]
    public void Tester_says_when_only_a_switched_off_rule_matches_and_when_nothing_matches()
    {
        var store = new MemoryDateRuleStore();
        var dialog = Dialog(store);
        dialog.TestName = "IMG-20240714-WA0001.jpg";

        dialog.Items.Single(i => i.Id == "whatsapp-android").IsEnabled = false;

        Assert.Equal(RuleTestKind.MatchSwitchedOff, dialog.TestKind);
        Assert.Equal("Rule \"WhatsApp (Android)\": Sunday, July 14, 2024 (rule is switched off)", dialog.TestResult);

        dialog.TestName = "IMG_4711.JPG";
        Assert.Equal(RuleTestKind.NoMatch, dialog.TestKind);
        Assert.Equal("No rule matches", dialog.TestResult);

        dialog.TestName = "  ";
        Assert.Equal(RuleTestKind.Empty, dialog.TestKind);
    }

    [Theory]
    [InlineData("IMG-20240714-WA0001.jpg", "IMG-20240714-WA0001")]
    [InlineData("VID-20240714-WA0001.MP4", "VID-20240714-WA0001")]
    [InlineData("WhatsApp Image 2024-07-14 at 15.32.08", "WhatsApp Image 2024-07-14 at 15.32.08")]
    [InlineData(" Scan_2023-02-01_7.png ", "Scan_2023-02-01_7")]
    [InlineData("", "")]
    public void Tester_only_removes_photo_and_video_extensions(string text, string expected) =>
        Assert.Equal(expected, DateRulesViewModel.NameWithoutExtension(text));

    [Fact]
    public void Picker_offers_at_most_fifty_distinct_names_and_fills_the_tester()
    {
        var dialog = Dialog(new MemoryDateRuleStore());

        dialog.Open(Enumerable.Range(0, 80).Select(i => $"IMG-202407{i % 30 + 1:00}-WA{i:0000}.jpg").Append("IMG-20240701-WA0000.JPG"));

        Assert.Equal(DateRulesViewModel.MaxSampleNames, dialog.SampleNames.Count);
        Assert.True(dialog.HasSampleNames);
        dialog.SelectedSample = dialog.SampleNames[3];
        Assert.Equal(dialog.SampleNames[3], dialog.TestName);
        Assert.Equal(RuleTestKind.Match, dialog.TestKind);
    }

    [Fact]
    public Task Picker_lists_names_with_a_file_date_first() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/DSC00012.jpg", new DateTime(2024, 7, 14, 16, 1, 55), camera: "ILCE-7M4");
        Jpeg(temp, "in/IMG-20240716-WA0003.jpg");
        Jpeg(temp, "in/Scan.jpg");
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;

        vm.OpenDateRulesCommand.Execute(null);

        Assert.True(vm.RulesDialog!.IsOpen);
        Assert.Equal("Scan.jpg", vm.RulesDialog.SampleNames[0]);
        Assert.Equal(3, vm.RulesDialog.SampleNames.Count);
    });

    // ---------- Changes ----------

    [Fact]
    public Task Switching_a_built_in_rule_off_is_saved_at_once_and_rescans_once_on_close() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg");
        var store = new MemoryDateRuleStore();
        var vm = new TestServices { DateRules = store }.CreateViewModel();
        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;
        Assert.Equal(BadgeKind.FileName, vm.VisibleItems.Single().Badge);
        var scans = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Phase) && vm.Phase == Phase.Scanning)
            {
                scans++;
            }
        };

        vm.OpenDateRulesCommand.Execute(null);
        var android = vm.RulesDialog!.Items.Single(i => i.Id == "whatsapp-android");
        android.IsEnabled = false;
        android.IsEnabled = true;
        android.IsEnabled = false;

        Assert.Equal(3, store.Saves);
        Assert.Equal(["whatsapp-android"], store.User!.DisabledSystemRuleIds);
        Assert.Equal(0, scans);

        vm.RulesDialog.CloseCommand.Execute(null);
        await vm.ScanCompletion;

        Assert.Equal(1, scans);
        Assert.False(vm.RulesDialog.IsOpen);
        Assert.Equal(BadgeKind.FileDate, vm.VisibleItems.Single().Badge);
        Assert.False(vm.DateRules!.Rules.Single(r => r.Id == "whatsapp-android").Enabled);

        // Opening and closing without a change does not scan again.
        vm.OpenDateRulesCommand.Execute(null);
        vm.RulesDialog.CloseCommand.Execute(null);
        await vm.ScanCompletion;
        Assert.Equal(1, scans);
    });

    [Fact]
    public Task Changing_rules_without_a_folder_does_not_scan() => RunOnUi(() =>
    {
        var store = new MemoryDateRuleStore();
        var vm = new TestServices { DateRules = store }.CreateViewModel();

        vm.OpenDateRulesCommand.Execute(null);
        vm.RulesDialog!.Items[0].IsEnabled = false;
        vm.RulesDialog.CloseCommand.Execute(null);

        Assert.Equal(Phase.Setup, vm.Phase);
        Assert.Equal(1, store.Saves);
    });

    [Theory]
    [InlineData("en-US", "WhatsApp (Android) (copy)")]
    [InlineData("de-DE", "WhatsApp (Android) (Kopie)")]
    public void Duplicate_adds_an_own_copy_before_the_built_in_rules(string culture, string copyName)
    {
        var store = new MemoryDateRuleStore();
        var dialog = Dialog(store, culture);

        dialog.Items.Single(i => i.Id == "whatsapp-android").DuplicateCommand.Execute(null);

        var copy = dialog.Items[0];
        Assert.Equal(copyName, copy.Name);
        Assert.True(copy.IsCustom);
        Assert.True(copy.IsEnabled);
        Assert.Equal(copy.Id, dialog.FocusRuleId);
        Assert.Equal(3, dialog.Items.Count);
        Assert.Equal(1, store.Saves);
        Assert.Equal(copyName, store.User!.CustomRules.Single().Name);
        Assert.True(dialog.Items.Single(i => i.Id == "whatsapp-android").IsEnabled, "The original stays on.");
    }

    [Fact]
    public void Duplicate_shortens_a_long_name_so_the_copy_stays_valid()
    {
        var longName = new string('x', UserDateRules.MaxNameLength);
        var rule = new DateRule("custom-1111aaaa", longName, RuleOrigin.Custom, Enabled: true, ["*{YYYY}{MM}{DD}*"]);
        var system = new DateRule("long", longName, RuleOrigin.System, Enabled: true, ["*{YYYY}{MM}{DD}*"]);
        var store = new MemoryDateRuleStore([system], new UserDateRules([], [rule]));
        var dialog = Dialog(store);

        dialog.Items.Single(i => i.Id == "long").DuplicateCommand.Execute(null);

        var copy = store.User!.CustomRules[^1];
        Assert.True(UserDateRules.IsValidName(copy.Name));
        Assert.EndsWith("… (copy)", copy.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_asks_inline_and_esc_cancels_before_it_closes()
    {
        var store = new MemoryDateRuleStore(userRules: new UserDateRules([], [MyScanner]));
        var dialog = Dialog(store);
        dialog.Open([]);
        var mine = dialog.Items[0];

        mine.DeleteCommand.Execute(null);
        Assert.True(mine.IsConfirmingDelete);
        Assert.False(mine.ShowActions);

        dialog.Escape();
        Assert.False(mine.IsConfirmingDelete);
        Assert.True(dialog.IsOpen);
        Assert.Equal(0, store.Saves);

        mine.DeleteCommand.Execute(null);
        mine.CancelDeleteCommand.Execute(null);
        Assert.Equal(3, dialog.Items.Count);

        mine.DeleteCommand.Execute(null);
        mine.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(2, dialog.Items.Count);
        Assert.Empty(store.User!.CustomRules);
        Assert.Equal(1, store.Saves);
        Assert.Equal("whatsapp-android", dialog.FocusRuleId);

        dialog.Escape();
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void A_change_made_elsewhere_reloads_the_list_and_says_so()
    {
        var store = new MemoryDateRuleStore();
        var dialog = Dialog(store);
        // Another Snapdate window switches the desktop rule off meanwhile.
        store.Write(new UserDateRules(["whatsapp-desktop"], []));

        dialog.Items.Single(i => i.Id == "whatsapp-android").IsEnabled = false;

        Assert.Equal(Strings().RulesConflict, dialog.MessageText);
        Assert.True(dialog.HasMessage);
        Assert.True(dialog.Items.Single(i => i.Id == "whatsapp-android").IsEnabled, "The refused change is undone.");
        Assert.False(dialog.Items.Single(i => i.Id == "whatsapp-desktop").IsEnabled, "The other window's change is shown.");
        Assert.Equal(0, store.Saves);

        // The next change is based on the reloaded file and is saved.
        dialog.Items.Single(i => i.Id == "whatsapp-android").IsEnabled = false;
        Assert.Equal(1, store.Saves);
        Assert.False(dialog.HasMessage);
        Assert.Equal(["whatsapp-desktop", "whatsapp-android"], store.User!.DisabledSystemRuleIds);
    }

    [Theory]
    [InlineData(DateRuleSaveResult.Failed, nameof(LocalizedStrings.RulesSaveFailed))]
    [InlineData(DateRuleSaveResult.TooLarge, nameof(LocalizedStrings.RulesTooLarge))]
    public void A_change_that_cannot_be_saved_is_undone_and_explained(DateRuleSaveResult result, string message)
    {
        var store = new ScriptedStore(UserRulesStatus.Ok) { SaveResult = result };
        var dialog = Dialog(store);

        dialog.Items[0].IsEnabled = false;

        Assert.True(dialog.Items[0].IsEnabled);
        Assert.Equal(Strings().Get(message), dialog.MessageText);
    }

    [Theory]
    [InlineData(DateRuleSaveResult.Failed, 0)]
    [InlineData(DateRuleSaveResult.TooLarge, 0)]
    [InlineData(DateRuleSaveResult.Conflict, 1)]
    public Task Only_a_change_made_elsewhere_rescans_after_a_refused_save(DateRuleSaveResult result, int expectedScans) => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg");
        var vm = new TestServicesWithStore(new ScriptedStore(UserRulesStatus.Ok) { SaveResult = result }).CreateViewModel("en-US");
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
        vm.RulesDialog!.Items[0].IsEnabled = false;
        vm.RulesDialog.CloseCommand.Execute(null);
        await vm.ScanCompletion;

        Assert.Equal(expectedScans, scans);
    });

    [Fact]
    public void Picking_the_same_name_again_after_editing_fills_the_tester_again()
    {
        var dialog = Dialog(new MemoryDateRuleStore());
        dialog.Open(["IMG-20240714-WA0001.jpg", "Scan.jpg"]);

        dialog.SelectedSample = "IMG-20240714-WA0001.jpg";
        dialog.TestName = "IMG-20240714-WA0001";

        Assert.Null(dialog.SelectedSample);
        dialog.SelectedSample = "IMG-20240714-WA0001.jpg";
        Assert.Equal("IMG-20240714-WA0001.jpg", dialog.TestName);
        Assert.Equal("IMG-20240714-WA0001.jpg", dialog.SelectedSample);
    }

    [Theory]
    [InlineData("en-US", "WhatsApp (Android), Built-in, off", "WhatsApp Desktop / Web, Built-in, on")]
    [InlineData("de-DE", "WhatsApp (Android), Mitgeliefert, ausgeschaltet", "WhatsApp Desktop / Web, Mitgeliefert, eingeschaltet")]
    public void Read_only_rows_say_whether_the_rule_is_on(string culture, string first, string second)
    {
        var store = new ReadOnlyWithDisabled();

        var dialog = Dialog(store, culture);

        Assert.Equal(first, dialog.Items[0].Accessible);
        Assert.Equal(second, dialog.Items[1].Accessible);
        Assert.Equal(culture == "en-US" ? "WhatsApp (Android), Built-in" : "WhatsApp (Android), Mitgeliefert",
            Dialog(new MemoryDateRuleStore(), culture).Items[0].Accessible);
    }

    [Fact]
    public void Unreadable_own_rules_show_a_warning_while_built_in_rules_stay_listed_and_active()
    {
        var dialog = Dialog(new ScriptedStore(UserRulesStatus.Unreadable));

        Assert.Equal(Strings().RulesUnreadable, dialog.StatusText);
        Assert.True(dialog.CanChange);
        Assert.Equal(2, dialog.Items.Count);
        Assert.All(dialog.Items, i => Assert.True(i.IsEnabled && i.CanChange && i.IsSystem));
        Assert.NotNull(dialog.RuleSet.Match("IMG-20240714-WA0001", DateTime.Now));
    }

    [Theory]
    [InlineData(UserRulesStatus.NewerVersion, nameof(LocalizedStrings.RulesNewerVersion))]
    [InlineData(UserRulesStatus.Inaccessible, nameof(LocalizedStrings.RulesInaccessible))]
    public void Read_only_rules_cannot_be_switched_duplicated_or_deleted(UserRulesStatus status, string message)
    {
        var store = new ScriptedStore(status, [MyScanner]);
        var dialog = Dialog(store);

        Assert.Equal(Strings().Get(message), dialog.StatusText);
        Assert.False(dialog.CanChange);
        Assert.All(dialog.Items, i => Assert.False(i.CanChange || i.CanDuplicate || i.CanDelete || i.CanEdit));
        Assert.False(dialog.CanAdd);

        dialog.Items[0].IsEnabled = false;

        Assert.True(dialog.Items[0].IsEnabled);
        Assert.Equal(Strings().RulesReadOnly, dialog.MessageText);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public Task Without_a_rule_store_there_is_no_link() => RunOnUi(() =>
    {
        var vm = new TestServices { DateRules = null }.CreateViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Settle(window);

        Assert.Null(vm.RulesDialog);
        Assert.False(window.FindControl<Button>("DateRulesLink")!.IsVisible);
        window.Close();
    });

    // ---------- Window ----------

    [Fact]
    public Task Link_opens_the_dialog_and_esc_closes_it_and_returns_focus() => RunOnUi(() =>
    {
        var vm = new TestServices().CreateViewModel();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();
        Settle(window);
        var link = window.FindControl<Button>("DateRulesLink")!;
        Assert.True(link.IsEffectivelyVisible);
        Assert.Equal("Dates from file names", AutomationPropertiesName(link));

        link.Focus(NavigationMethod.Tab);
        var center = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);

        Assert.True(vm.RulesDialog!.IsOpen);
        var root = DialogRoot(window);
        Assert.True(root.IsEffectivelyVisible);
        var focused = Focused(window);
        Assert.IsType<ToggleSwitch>(focused);
        Assert.True(root.IsVisualAncestorOf(focused));
        Assert.Equal("WhatsApp (Android) on/off", AutomationPropertiesName(focused));

        Press(window, PhysicalKey.Escape);

        Assert.False(vm.RulesDialog.IsOpen);
        Assert.False(root.IsEffectivelyVisible);
        Assert.Same(link, Focused(window));
        window.Close();
    });

    [Fact]
    public Task Close_button_closes_the_dialog() => RunOnUi(() =>
    {
        var vm = new TestServices().CreateViewModel("de-DE");
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.OpenDateRulesCommand.Execute(null);
        Settle(window);

        var close = window.FindControl<DateRulesDialog>("DateRulesDialog")!.FindControl<Button>("CloseButton")!;
        Assert.Equal("Schließen", AutomationPropertiesName(close));
        close.Command!.Execute(null);
        Settle(window);

        Assert.False(vm.RulesDialog!.IsOpen);
        window.Close();
    });

    [Fact]
    public Task Dialog_works_with_the_keyboard_alone() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg");
        var store = new MemoryDateRuleStore();
        var vm = new TestServices { DateRules = store }.CreateViewModel();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();
        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;
        Settle(window);

        var link = window.FindControl<Button>("DateRulesLink")!;
        link.Focus(NavigationMethod.Tab);
        Press(window, PhysicalKey.Enter);
        Assert.True(vm.RulesDialog!.IsOpen);
        var root = DialogRoot(window);

        // Space switches the focused rule off; the change is saved at once.
        var toggle = Assert.IsType<ToggleSwitch>(Focused(window));
        Press(window, PhysicalKey.Space);
        Assert.False(toggle.IsChecked);
        Assert.Equal(1, store.Saves);

        // Tab goes top to bottom (switch, Duplicate, next switch, …, tester, Close) and stays inside the dialog.
        Press(window, PhysicalKey.Tab);
        var duplicate = Assert.IsType<Button>(Focused(window));
        Assert.Equal("Duplicate WhatsApp (Android)", AutomationPropertiesName(duplicate));
        Press(window, PhysicalKey.Enter);
        Assert.Equal(2, store.Saves);
        Assert.Equal("WhatsApp (Android) (copy) on/off", AutomationPropertiesName(Focused(window)));

        var order = new List<Control>();
        for (var i = 0; i < 20; i++)
        {
            Press(window, PhysicalKey.Tab);
            var focused = Focused(window);
            Assert.True(root.IsVisualAncestorOf(focused), $"Focus left the dialog: {focused}");
            order.Add(focused);
        }

        Assert.Contains(order, c => c.Name == "TestNameBox" || c.FindAncestorOfType<TextBox>()?.Name == "TestNameBox");
        Assert.Contains(order, c => c.Name == "SamplePicker");
        Assert.Contains(order, c => c.Name == "CloseButton");
        var tops = order.TakeWhile(c => c.Name != "CloseButton")
            .Select(c => c.TranslatePoint(default, root)!.Value.Y).ToList();
        Assert.Equal(tops.Order(), tops);

        // Typing into the tester shows the result; Esc closes and the folder is scanned again.
        window.FindControl<DateRulesDialog>("DateRulesDialog")!.FindControl<TextBox>("TestNameBox")!.Focus(NavigationMethod.Tab);
        window.KeyTextInput("IMG-20160706-WA0002.jpg");
        Settle(window);
        Assert.Equal(RuleTestKind.Match, vm.RulesDialog.TestKind);
        Assert.Contains("(copy)", vm.RulesDialog.TestResult, StringComparison.Ordinal);

        Press(window, PhysicalKey.Escape);
        Assert.False(vm.RulesDialog.IsOpen);
        await vm.ScanCompletion;
        Assert.Equal("WhatsApp (Android) (copy)", vm.VisibleItems.Single().Item.Photo.ResolvedDate.RuleName);
        window.Close();
    });

    [Fact]
    public Task Keyboard_focus_stays_on_the_rule_when_a_change_is_refused() => RunOnUi(() =>
    {
        var store = new MemoryDateRuleStore();
        var vm = new TestServices { DateRules = store }.CreateViewModel();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();
        Settle(window);
        window.FindControl<Button>("DateRulesLink")!.Focus(NavigationMethod.Tab);
        Press(window, PhysicalKey.Enter);
        Press(window, PhysicalKey.Tab);
        Press(window, PhysicalKey.Tab);
        Assert.Equal("WhatsApp Desktop / Web on/off", AutomationPropertiesName(Focused(window)));

        // Another window changes the file; switching the rule here is refused and the list is reloaded.
        store.Write(new UserDateRules(["whatsapp-android"], []));
        Press(window, PhysicalKey.Space);

        Assert.Equal(vm.L.RulesConflict, vm.RulesDialog!.MessageText);
        var focused = Assert.IsType<ToggleSwitch>(Focused(window));
        Assert.True(DialogRoot(window).IsVisualAncestorOf(focused));
        Assert.Equal("WhatsApp Desktop / Web on/off", AutomationPropertiesName(focused));
        Assert.True(focused.IsChecked);

        // Space again now works on the reloaded rules.
        Press(window, PhysicalKey.Space);
        Assert.Equal(["whatsapp-android", "whatsapp-desktop"], store.User!.DisabledSystemRuleIds);
        window.Close();
    });

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public Task Dialog_fits_the_smallest_window(string culture) => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg");
        Jpeg(temp, "in/Scan_2023-02-01_7.jpg");
        // The tallest realistic state: a warning, own rules with long names and patterns, a folder for the picker.
        var custom = Enumerable.Range(0, 6)
            .Select(i => new DateRule(
                $"custom-0000000{i}",
                new string('W', UserDateRules.MaxNameLength),
                RuleOrigin.Custom,
                Enabled: true,
                [.. Enumerable.Repeat("Scan_{YYYY}-{MM}-{DD}_*", 10)]))
            .ToList();
        var store = new ScriptedStore(UserRulesStatus.Unreadable, custom);
        var withStore = new TestServicesWithStore(store).CreateViewModel(culture);
        var window = new MainWindow { DataContext = withStore, Width = 980, Height = 720 };
        window.Show();
        withStore.SourceFolder = temp.Combine("in");
        await withStore.ScanCompletion;
        withStore.OpenDateRulesCommand.Execute(null);
        withStore.RulesDialog!.Items[0].DeleteCommand.Execute(null);
        withStore.RulesDialog.TestName = "WhatsApp Image 2024-07-14 at 15.32.08.jpeg";
        Settle(window);

        var root = DialogRoot(window);
        var rootTopLeft = root.TranslatePoint(default, window)!.Value;
        Assert.True(rootTopLeft.Y >= 0 && rootTopLeft.Y + root.Bounds.Height <= 720.5, $"Dialog is {root.Bounds.Height:0} px tall.");
        Assert.True(rootTopLeft.X >= 0 && rootTopLeft.X + root.Bounds.Width <= 980.5);

        // Nothing sticks out sideways; outside the scrolling list nothing is cut off at the bottom either.
        var scroller = root.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "RuleScroller");
        foreach (var control in root.GetVisualDescendants().OfType<Control>()
                     .Where(c => c is TextBlock or Button or ToggleSwitch or TextBox or ComboBox && c.IsEffectivelyVisible))
        {
            var topLeft = control.TranslatePoint(default, root)!.Value;
            Assert.True(topLeft.X + control.Bounds.Width <= root.Bounds.Width + 0.5, $"{control} sticks out ({culture}).");
            if (!scroller.IsVisualAncestorOf(control))
            {
                Assert.True(topLeft.Y + control.Bounds.Height <= root.Bounds.Height + 0.5, $"{control} is cut off ({culture}).");
            }
        }

        // In every row the name, badges and patterns end before the actions (or the delete confirmation) begin.
        var rows = root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ruleRow")).ToList();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var actions = row.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => Grid.GetColumn(p) == 2 && p.IsEffectivelyVisible);
            var actionsLeft = actions.TranslatePoint(default, row)!.Value.X;
            foreach (var part in row.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is TextBlock or Border && c.IsEffectivelyVisible && !actions.IsVisualAncestorOf(c) && c != actions))
            {
                if (part is Border { Classes.Count: 0 } || part.FindAncestorOfType<ToggleSwitch>() is not null)
                {
                    continue;
                }

                var right = part.TranslatePoint(new Point(part.Bounds.Width, 0), row)!.Value.X;
                Assert.True(right <= actionsLeft + 0.5, $"{part} ({(part as TextBlock)?.Text}) runs into the row actions ({culture}).");
            }
        }

        // Buttons and short texts are never shortened.
        foreach (var text in root.GetVisualDescendants().OfType<TextBlock>()
                     .Where(t => t.IsEffectivelyVisible && t.TextTrimming == Avalonia.Media.TextTrimming.None && t.TextWrapping == Avalonia.Media.TextWrapping.NoWrap))
        {
            text.Measure(Size.Infinity);
            var needed = text.DesiredSize.Width - text.Margin.Left - text.Margin.Right;
            Assert.True(needed <= text.Bounds.Width + 0.5, $"\"{text.Text}\" is clipped ({culture}).");
        }

        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "The long list scrolls.");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == withStore.L.RulesUnreadable);

        // The link fits next to the photo count in the preview header.
        var link = window.FindControl<Button>("DateRulesLink")!;
        var header = link.GetVisualParent<Grid>()!;
        Assert.True(link.TranslatePoint(default, header)!.Value.X + link.Bounds.Width <= header.Bounds.Width + 6.5);
        window.Close();
    });

    private static string? AutomationPropertiesName(Control control) =>
        Avalonia.Automation.AutomationProperties.GetName(control);

    /// <summary>A rule store whose user file has the given status and whose saves give the given result.</summary>
    private sealed class ScriptedStore(UserRulesStatus status, IReadOnlyList<DateRule>? custom = null) : IDateRuleStore
    {
        private static readonly IReadOnlyList<DateRule> BuiltIn = new MemoryDateRuleStore().LoadSystemRules();

        public DateRuleSaveResult SaveResult { get; init; } = DateRuleSaveResult.Saved;

        public int Saves { get; private set; }

        public IReadOnlyList<DateRule> LoadSystemRules() => BuiltIn;

        // A real unreadable file gives no rules; the layout test still passes some to fill the list.
        public UserDateRulesLoad LoadUserRules() =>
            new(new UserDateRules([], custom ?? []), status, UserRulesFingerprint.ForContent(3, "ABC"));

        public UserDateRulesSave SaveUserRules(UserDateRules rules, UserRulesFingerprint expected)
        {
            if (SaveResult == DateRuleSaveResult.Saved)
            {
                Saves++;
            }

            return new UserDateRulesSave(SaveResult, expected);
        }
    }

    /// <summary>A file written by a newer version that switched the Android rule off.</summary>
    private sealed class ReadOnlyWithDisabled : IDateRuleStore
    {
        public IReadOnlyList<DateRule> LoadSystemRules() => new MemoryDateRuleStore().LoadSystemRules();

        public UserDateRulesLoad LoadUserRules() =>
            new(new UserDateRules(["whatsapp-android"], []), UserRulesStatus.NewerVersion, UserRulesFingerprint.ForContent(3, "ABC"));

        public UserDateRulesSave SaveUserRules(UserDateRules rules, UserRulesFingerprint expected) =>
            new(DateRuleSaveResult.ReadOnly, expected);
    }

    /// <summary><see cref="TestServices"/> with any rule store.</summary>
    private sealed class TestServicesWithStore(IDateRuleStore store)
    {
        public MainWindowViewModel CreateViewModel(string culture) =>
            new(
                new AppServices(
                    new FakeTheme(),
                    new FakeDesktop(),
                    new MemorySettings(),
                    new Snapdate.Infrastructure.FileSystem.LocalFileSystem(),
                    new Snapdate.Infrastructure.Metadata.MetadataExtractorReader(TimeProvider.System),
                    TimeProvider.System,
                    null,
                    DateRules: store),
                CultureInfo.GetCultureInfo(culture));
    }
}
