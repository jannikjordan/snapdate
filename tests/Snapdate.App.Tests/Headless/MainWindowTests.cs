using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
}
