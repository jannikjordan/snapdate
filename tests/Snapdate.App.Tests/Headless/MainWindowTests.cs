using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Snapdate.App.Controls;
using Snapdate.App.Services;
using Snapdate.App.Tests.Support;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Application.Localization;
using Snapdate.Application.Settings;

namespace Snapdate.App.Tests.Headless;

public sealed class MainWindowTests(HeadlessSession headless)
{
    private static List<string> VisibleTexts(Window window) =>
        window.GetLogicalDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

    private static bool IsShown(Window window, string text) =>
        window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);

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

    [Fact]
    public Task Window_starts_in_German_on_a_German_system() => RunOnUi(() =>
    {
        var vm = new TestServices().CreateViewModel("de-DE");
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UiLanguage.German, vm.Language);
        Assert.Contains("Ordner wählen", VisibleTexts(window));
        window.Close();
    });

    [Fact]
    public Task Remembered_language_wins_over_windows_language() => RunOnUi(() =>
    {
        var services = new TestServices { Settings = new MemorySettings(new AppSettings { Language = UiLanguage.English }) };
        Assert.Equal(UiLanguage.English, services.CreateViewModel("de-DE").Language);
    });

    [Fact]
    public Task Switching_language_updates_the_visible_text() => RunOnUi(() =>
    {
        var vm = new TestServices().CreateViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Choose folders", VisibleTexts(window));

        vm.IsGerman = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Ordner wählen", VisibleTexts(window));
        Assert.DoesNotContain("Choose folders", VisibleTexts(window));
        window.Close();
    });

    [Theory]
    [InlineData("en-US", "user-guide.en.md")]
    [InlineData("de-DE", "user-guide.de.md")]
    public Task Help_opens_the_guide_in_the_ui_language(string culture, string expectedFile) => RunOnUi(async () =>
    {
        var services = new TestServices();
        var vm = services.CreateViewModel(culture);

        await vm.OpenHelpCommand.ExecuteAsync(null);

        var url = Assert.Single(services.Desktop.OpenedUrls);
        Assert.StartsWith(AppLinks.Repository, url.ToString(), StringComparison.Ordinal);
        Assert.EndsWith(expectedFile, url.ToString(), StringComparison.Ordinal);
    });

    [Fact]
    public Task Copy_is_the_safe_default() => RunOnUi(() =>
    {
        var vm = new TestServices().CreateViewModel();
        Assert.True(vm.IsCopyMode);
        Assert.False(vm.CanRun);
    });

    [Fact]
    public Task Theme_toggle_delegates_to_theme_service() => RunOnUi(() =>
    {
        var services = new TestServices();
        var vm = services.CreateViewModel();

        vm.ToggleThemeCommand.Execute(null);

        Assert.Equal(1, services.Theme.Toggles);
    });

    [Fact]
    public Task Invalid_pattern_marks_the_preview_stale_until_the_pattern_is_valid_again() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 7, 14, 15, 32, 8));
        temp.Photo("camera/b.png", new DateTime(2024, 7, 15, 8, 0, 0));
        var target = temp.Combine("sorted");
        Directory.CreateDirectory(target);
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;
        vm.TargetFolder = target;
        vm.Pattern.Text = "{YYYY}-{MM}-{DD}";
        Assert.False(vm.IsPreviewStale);
        Assert.Equal("2024-07-14.jpg", vm.VisibleItems[0].NewName);

        vm.Pattern.Text = "{YYYY}-{nope}";

        Assert.True(vm.IsPreviewStale);
        Assert.True(vm.ShowList, "The list stays so the user keeps orientation.");
        Assert.Equal(2, vm.CountAll);
        Assert.False(vm.CanRun);

        vm.Pattern.Text = "{YYYY}{MM}{DD}";

        Assert.False(vm.IsPreviewStale);
        Assert.Equal("20240714.jpg", vm.VisibleItems[0].NewName);
        Assert.True(vm.CanRun);
    });

    [Fact]
    public Task Stale_preview_shows_a_hint_and_drops_the_numbered_filter() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 7, 14, 15, 32, 8));
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");
        vm.Filter = PreviewFilter.Renamed;
        var window = new MainWindow { DataContext = vm };
        window.Show();

        vm.Pattern.Text = "{YYYY}-{nope}";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(PreviewFilter.All, vm.Filter);
        Assert.Contains(vm.L.PreviewStaleTitle, VisibleTexts(window));
        Assert.Contains(vm.L.PreviewStaleBody, VisibleTexts(window));

        vm.Pattern.Text = "{YYYY}";
        Dispatcher.UIThread.RunJobs();

        Assert.False(IsShown(window, vm.L.PreviewStaleTitle));
        window.Close();
    });

    [Fact]
    public Task Stale_preview_follows_a_folder_change_and_empties_when_there_is_nothing_to_show() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("one/a.jpg", new DateTime(2024, 7, 14, 15, 32, 8));
        temp.Photo("two/b.jpg", new DateTime(2023, 1, 2, 3, 4, 5));
        Directory.CreateDirectory(temp.Combine("empty"));
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("one");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");
        vm.Pattern.Text = "{YYYY}";
        vm.Pattern.Text = "{YYYY}-{nope}";

        vm.SourceFolder = temp.Combine("two");
        await vm.ScanCompletion;

        Assert.True(vm.IsPreviewStale);
        Assert.Equal("2023.jpg", vm.VisibleItems.Single().NewName);

        vm.SourceFolder = temp.Combine("empty");
        await vm.ScanCompletion;

        Assert.Empty(vm.VisibleItems);
        Assert.Equal(0, vm.CountAll);
        Assert.False(vm.IsPreviewStale);
    });

    [Fact]
    public Task Stale_preview_hides_the_numbered_badge_on_rows() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("one/a.jpg", new DateTime(2024, 7, 14, 15, 32, 8));
        temp.Photo("one/b.jpg", new DateTime(2024, 7, 14, 15, 32, 8));
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("one");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");
        vm.Pattern.Text = "{YYYY}";
        Assert.All(vm.VisibleItems, i => Assert.True(i.ShowBadge));

        vm.Pattern.Text = "{nope}";

        Assert.Contains(vm.VisibleItems, i => i.NewName.Contains("(2)"));
        Assert.All(vm.VisibleItems, i => Assert.False(i.Badge == BadgeKind.Renamed && i.ShowBadge));
    });

    [Fact]
    public Task Invalid_saved_pattern_at_startup_shows_a_stale_list_built_from_the_default_pattern() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("one/a.jpg", new DateTime(2024, 7, 14, 15, 32, 8));
        var services = new TestServices
        {
            Settings = new MemorySettings(new AppSettings { SourceFolder = temp.Combine("one"), Pattern = "{nope}" }),
        };

        var vm = services.CreateViewModel();
        await vm.ScanCompletion;

        Assert.True(vm.IsPreviewStale);
        Assert.Equal("2024-07-14_15-32-08.jpg", vm.VisibleItems.Single().NewName);
        Assert.False(vm.CanRun);
    });

    [Fact]
    public Task Full_flow_copies_photos_with_new_names() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 7, 14, 15, 32, 8));
        temp.Photo("camera/b.png", new DateTime(2024, 7, 15, 8, 0, 0));
        var target = temp.Combine("sorted");
        Directory.CreateDirectory(target);
        var vm = new TestServices().CreateViewModel();

        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;
        vm.TargetFolder = target;
        vm.Pattern.Text = "{YYYY}-{MM}-{DD} {HH}{mm}";

        Assert.Equal(2, vm.CountAll);
        Assert.True(vm.CanRun);
        Assert.Equal("2024-07-14 1532.jpg", vm.ExampleName);

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(Phase.Done, vm.Phase);
        Assert.True(File.Exists(Path.Combine(target, "2024-07-14 1532.jpg")));
        Assert.True(File.Exists(Path.Combine(target, "2024-07-15 0800.png")));
        Assert.True(File.Exists(temp.Combine("camera", "a.jpg")), "Copy must keep the original.");
        Assert.True(vm.HasLog);
    });

    [Fact]
    public Task Move_asks_for_confirmation_first() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 1, 1));
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");
        Directory.CreateDirectory(vm.TargetFolder);
        vm.IsMoveMode = true;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.IsConfirmMoveOpen);
        Assert.True(File.Exists(temp.Combine("camera", "a.jpg")), "Nothing may happen before confirming.");

        await vm.ConfirmMoveCommand.ExecuteAsync(null);

        Assert.False(File.Exists(temp.Combine("camera", "a.jpg")));
        Assert.Equal(Phase.Done, vm.Phase);
    });

    [Fact]
    public Task Invalid_pattern_blocks_running_and_explains_why() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 1, 1));
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");

        vm.Pattern.Text = "{YYYY}:{nope}";

        Assert.False(vm.CanRun);
        Assert.Equal(StatusKind.Warning, vm.StatusKind);
        Assert.True(vm.Pattern.HasError);
        Assert.Contains("1", vm.RunButtonText, StringComparison.Ordinal);
    });

    [Fact]
    public Task Target_inside_source_is_refused_when_including_sub_folders() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 1, 1));
        var vm = new TestServices().CreateViewModel();
        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;

        vm.TargetFolder = temp.Combine("camera", "sorted");

        Assert.False(vm.CanRun);
        Assert.Equal(vm.L.ProblemTargetInsideSource, vm.StatusTitle);
    });

    [Theory]
    [InlineData("en-US", 1180, 780)]
    [InlineData("de-DE", 1180, 780)]
    [InlineData("en-US", 980, 720)]
    [InlineData("de-DE", 980, 720)]
    public Task Left_column_fits_without_scrolling(string culture, int width, int height) => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 1, 1));
        var vm = new TestServices().CreateViewModel(culture);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");
        Directory.CreateDirectory(vm.TargetFolder);

        // A long pattern that wraps the pill row is the tallest realistic state.
        vm.Pattern.Text = "{YYYY}/{MM}/{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss} {name}";
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var left = window.GetLogicalDescendants().OfType<ScrollViewer>().First();
        Assert.True(
            left.Extent.Height <= left.Viewport.Height + 0.5,
            $"Left column needs {left.Extent.Height:0} px but only {left.Viewport.Height:0} px are visible.");
        window.Close();
    });

    [Theory]
    [InlineData("en-US", "Copy 1 photo", "Ready: 1 photo", "1 photo")]
    [InlineData("de-DE", "1 Foto kopieren", "Bereit: 1 Foto", "1 Foto")]
    public Task A_single_photo_uses_singular_wording(string culture, string button, string status, string count) => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        temp.Photo("camera/a.jpg", new DateTime(2024, 1, 1));
        var vm = new TestServices().CreateViewModel(culture);
        vm.SourceFolder = temp.Combine("camera");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");
        Directory.CreateDirectory(vm.TargetFolder);

        Assert.Equal(button, vm.RunButtonText);
        Assert.Equal(status, vm.StatusTitle);
        Assert.Equal(count, vm.PhotosCountText);
    });

    [Fact]
    public Task Run_button_has_no_zero_count_before_photos_are_found() => RunOnUi(() =>
    {
        Assert.Equal("Copy photos", new TestServices().CreateViewModel().RunButtonText);
        Assert.Equal("Fotos kopieren", new TestServices().CreateViewModel("de-DE").RunButtonText);
    });

    [Fact]
    public Task Settings_are_saved_without_forcing_a_language() => RunOnUi(() =>
    {
        var services = new TestServices();
        var vm = services.CreateViewModel("de-DE");
        vm.Pattern.Text = "{YYYY} {name}";

        vm.SaveSettings();

        Assert.Equal("{YYYY} {name}", services.Settings.Current.Pattern);
        Assert.Null(services.Settings.Current.Language);

        vm.IsEnglish = true;
        vm.SaveSettings();
        Assert.Equal(UiLanguage.English, services.Settings.Current.Language);
    });

    [Fact]
    public Task Saving_ui_settings_preserves_update_check_state() => RunOnUi(() =>
    {
        // Regression: SaveSettings used to build a fresh AppSettings and reset these fields.
        var checkedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var services = new TestServices
        {
            Settings = new MemorySettings(new AppSettings { AutoUpdateCheck = false, LastUpdateCheckUtc = checkedAt, SkippedVersion = "0.2.0" }),
        };
        var vm = services.CreateViewModel("de-DE");

        vm.SaveSettings();

        Assert.False(services.Settings.Current.AutoUpdateCheck);
        Assert.Equal(checkedAt, services.Settings.Current.LastUpdateCheckUtc);
        Assert.Equal("0.2.0", services.Settings.Current.SkippedVersion);
    });

    [Fact]
    public Task Title_bar_is_the_caption_area_and_its_controls_are_not() => RunOnUi(() =>
    {
        var window = new MainWindow { DataContext = new TestServices().CreateViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The window keeps its title for Alt-Tab, the taskbar and screen readers.
        Assert.Equal("Snapdate", window.Title);

        // The bar itself is what Windows reports as HTCAPTION: drag, double-click, snap layouts.
        var titleBar = window.FindControl<Border>("TitleBar");
        Assert.NotNull(titleBar);
        Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(titleBar));

        // Logo, name and version only decorate the bar and must not swallow the drag.
        var passive = titleBar.GetLogicalDescendants()
            .Where(c => c is BrandLogo || c is TextBlock { Text: "Snapdate" })
            .OfType<InputElement>()
            .ToList();
        Assert.NotEmpty(passive);
        Assert.All(passive, v => Assert.False(v.IsHitTestVisible));

        // Every interactive control stays a client area (HTCLIENT) so it receives clicks.
        var interactive = titleBar.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Equal(5, interactive.Count); // EN, DE, theme, help, about
        foreach (var control in interactive)
        {
            var owner = control.GetSelfAndLogicalAncestors().OfType<Visual>()
                .First(v => WindowDecorationProperties.GetElementRole(v) != WindowDecorationsElementRole.None);
            Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(owner));
        }

        window.Close();
    });
}
