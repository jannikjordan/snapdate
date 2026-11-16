using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Application.Updates;
using Snapdate.Infrastructure.FileSystem;
using Snapdate.Infrastructure.Installation;
using Snapdate.Infrastructure.Metadata;
using Snapdate.Infrastructure.Settings;
using Snapdate.Infrastructure.Time;
using Snapdate.Infrastructure.Updates;

namespace Snapdate.App;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: services are wired by hand (AOT-friendly, no reflection-based DI).
            Window? window = null;
            var settings = new JsonSettingsStore(JsonSettingsStore.DefaultPath);
            var desktopServices = new AvaloniaDesktopServices(() => window);

            // The update check is off (silently) when the running version is not a valid version number.
            GitHubReleaseFeed? feed = null;
            UpdateCheckService? updateCheck = null;
            if (RunningVersion.Parsed is { } running)
            {
                feed = new GitHubReleaseFeed(running);
                updateCheck = new UpdateCheckService(feed, settings, new SystemClock(), running);
            }

            var services = new AppServices(
                new AvaloniaThemeService(this),
                desktopServices,
                settings,
                new LocalFileSystem(),
                new MetadataExtractorReader(TimeProvider.System),
                TimeProvider.System,
                new ThumbnailLoader(),
                updateCheck,
                FileInstallInfo.ForCurrentProcess(),
                new OpenReleasePageUpdateInstaller(desktopServices));

            window = new MainWindow { DataContext = new MainWindowViewModel(services, CultureInfo.CurrentUICulture) };
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => feed?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
