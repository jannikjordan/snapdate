using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Infrastructure.FileSystem;
using Snapdate.Infrastructure.Metadata;
using Snapdate.Infrastructure.Settings;

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
            var services = new AppServices(
                new AvaloniaThemeService(this),
                new AvaloniaDesktopServices(() => window),
                new JsonSettingsStore(JsonSettingsStore.DefaultPath),
                new LocalFileSystem(),
                new MetadataExtractorReader(TimeProvider.System),
                TimeProvider.System,
                new ThumbnailLoader());

            window = new MainWindow { DataContext = new MainWindowViewModel(services, CultureInfo.CurrentUICulture) };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
