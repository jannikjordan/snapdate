using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;

namespace Snapdate.App;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: wire services by hand (AOT-friendly, no reflection DI).
            var viewModel = new MainWindowViewModel(new AvaloniaThemeService(this), CultureInfo.CurrentUICulture);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
