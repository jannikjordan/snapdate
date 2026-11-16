using System.Globalization;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Application.Localization;

namespace Snapdate.App.Tests.Headless;

public sealed class MainWindowTests(HeadlessSession headless)
{
    private sealed class FakeTheme : IThemeService
    {
        public bool IsDark { get; private set; }

        public int Toggles { get; private set; }

        public void Toggle()
        {
            IsDark = !IsDark;
            Toggles++;
        }
    }

    private static List<string> VisibleTexts(Window window) =>
        window.GetLogicalDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

    private Task RunOnUi(Action action) =>
        headless.Session.Dispatch(action, TestContext.Current.CancellationToken);

    [Fact]
    public Task Window_starts_in_German_on_a_German_system() => RunOnUi(() =>
    {
        var vm = new MainWindowViewModel(new FakeTheme(), CultureInfo.GetCultureInfo("de-DE"));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(UiLanguage.German, vm.Language);
        Assert.Contains("Ordner wählen", VisibleTexts(window));
        window.Close();
    });

    [Fact]
    public Task Switching_language_updates_the_visible_text() => RunOnUi(() =>
    {
        var vm = new MainWindowViewModel(new FakeTheme(), CultureInfo.GetCultureInfo("en-US"));
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

    [Fact]
    public Task Copy_is_the_default_mode() => RunOnUi(() =>
    {
        var vm = new MainWindowViewModel(new FakeTheme(), CultureInfo.GetCultureInfo("en-US"));
        Assert.True(vm.IsCopyMode);
    });

    [Fact]
    public Task Theme_toggle_command_delegates_to_theme_service() => RunOnUi(() =>
    {
        var theme = new FakeTheme();
        var vm = new MainWindowViewModel(theme, CultureInfo.GetCultureInfo("en-US"));

        vm.ToggleThemeCommand.Execute(null);

        Assert.Equal(1, theme.Toggles);
        Assert.True(theme.IsDark);
    });
}
