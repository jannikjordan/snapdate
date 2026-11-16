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
            var installInfo = FileInstallInfo.ForCurrentProcess();

            // The update check and the in-app update are off (silently) when the running version is not a valid
            // version number.
            GitHubReleaseFeed? feed = null;
            UpdateCheckService? updateCheck = null;
            HttpUpdateDownloader? downloader = null;
            EcdsaUpdateSignatureVerifier? verifier = null;
            InAppUpdateInstaller? updateInstaller = null;
            if (RunningVersion.Parsed is { } running)
            {
                feed = new GitHubReleaseFeed(running);
                updateCheck = new UpdateCheckService(feed, settings, new SystemClock(), running);

                downloader = new HttpUpdateDownloader(running);
                verifier = EcdsaUpdateSignatureVerifier.CreateDefault();
                updateInstaller = new InAppUpdateInstaller(new InstallUpdateUseCase(
                    running,
                    installInfo,
                    new RegistryInstallLocationCheck(),
                    new ProcessArchitecture(),
                    downloader,
                    verifier,
                    new ProcessInstallerLauncher()));

                // Leftovers of earlier update attempts; best effort, never throws, never blocks start-up.
                var stale = downloader;
                _ = Task.Run(stale.CleanUpStaleDownloads);
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
                installInfo,
                updateInstaller,
                new AvaloniaAppExit(desktop));

            var viewModel = new MainWindowViewModel(services, CultureInfo.CurrentUICulture);
            window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;

            // Setup closes a running Snapdate through Windows' Restart Manager, and Windows may end the session:
            // either way the app is asked to shut down, which must end the wait for a running installer. (The
            // window's Closing handler does the same and also saves the settings.)
            desktop.ShutdownRequested += (_, _) => viewModel.Update.NotifyShutdownRequested();
            desktop.Exit += (_, _) =>
            {
                feed?.Dispose();
                downloader?.Dispose();
                verifier?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
