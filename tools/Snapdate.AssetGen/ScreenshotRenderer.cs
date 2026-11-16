using System.Globalization;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;

namespace Snapdate.AssetGen;

/// <summary>Renders documentation screenshots from the real views, per language and theme.</summary>
internal static class ScreenshotRenderer
{
    private sealed class FixedTheme : IThemeService
    {
        public bool IsDark => false;

        public void Toggle()
        {
        }
    }

    private static readonly (string Folder, string Culture)[] Languages = [("en", "en-US"), ("de", "de-DE")];

    public static void Run(string repoRoot)
    {
        foreach (var (folder, culture) in Languages)
        {
            var dir = Path.Combine(repoRoot, "docs", "images", folder);
            Directory.CreateDirectory(dir);

            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Avalonia.Application.Current!.RequestedThemeVariant = variant;
                var suffix = variant == ThemeVariant.Dark ? "dark" : "light";
                Shoot(dir, $"main-start-{suffix}", CultureInfo.GetCultureInfo(culture));
            }
        }

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }

    private static void Shoot(string dir, string name, CultureInfo culture)
    {
        var vm = new MainWindowViewModel(new FixedTheme(), culture);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        var path = Path.Combine(dir, name + ".png");
        frame.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
        Console.WriteLine($"wrote {path}");
    }
}
