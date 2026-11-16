using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Application.Installation;
using Snapdate.Application.Ports;
using Snapdate.Application.Settings;
using Snapdate.Application.Updates;
using Snapdate.Domain.Versioning;
using Snapdate.Infrastructure.FileSystem;
using Snapdate.Infrastructure.Metadata;

namespace Snapdate.AssetGen;

/// <summary>Renders documentation screenshots from the real views, per language and theme.</summary>
internal static class ScreenshotRenderer
{
    // A neutral location so screenshots never show a personal user folder.
    private const string DemoRoot = @"C:\SnapdateDemo";

    private static readonly (string Folder, string Culture)[] Languages = [("en", "en-US"), ("de", "de-DE")];

    private sealed class FixedTheme : IThemeService
    {
        public bool IsDark => false;

        public string? CurrentChoice => null;

        public void Toggle()
        {
        }

        public void Apply(string? theme)
        {
        }
    }

    private sealed class NoDesktop : IDesktopServices
    {
        public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult<string?>(null);

        public Task OpenUrlAsync(Uri url) => Task.CompletedTask;

        public Task OpenFolderAsync(string path) => Task.CompletedTask;

        public Task OpenFileAsync(string path) => Task.CompletedTask;
    }

    private sealed class MemorySettings(AppSettings initial) : ISettingsStore
    {
        private AppSettings _current = initial;

        public AppSettings Load() => _current;

        public void Save(AppSettings settings) => _current = settings;
    }

    /// <summary>Never touches the network: answers with a fixed release.</summary>
    private sealed class FixedFeed(ReleaseInfo? latest) : IReleaseFeed
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(latest);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedInstall(InstallKind kind) : IInstallInfo
    {
        public InstallInfo Current { get; } = new(kind, kind == InstallKind.Installed ? InstallScope.CurrentUser : InstallScope.None);
    }

    /// <summary>How the update check behaves in a screenshot.</summary>
    private sealed record UpdateShot(bool NewerVersion = false, InstallKind Kind = InstallKind.Installed, bool NoticeShown = true);

    private static int _width = 1180;
    private static int _height = 780;

    /// <summary>Renders every state at the given window size into outputRoot/{en,de} (review use).</summary>
    public static void RunReview(string repoRoot, string outputRoot, int width, int height)
    {
        (_width, _height) = (width, height);
        Run(repoRoot, outputRoot);
    }

    public static void Run(string repoRoot, string? outputRoot = null)
    {
        if (Directory.Exists(DemoRoot))
        {
            Directory.Delete(DemoRoot, recursive: true);
        }

        var source = Path.Combine(DemoRoot, "Holiday 2024");
        SamplePhotos.Create(source);
        using var thumbnails = new ThumbnailLoader();

        try
        {
            foreach (var (folder, culture) in Languages)
            {
                var dir = Path.Combine(outputRoot ?? Path.Combine(repoRoot, "docs", "images"), folder);
                Directory.CreateDirectory(dir);
                var ci = CultureInfo.GetCultureInfo(culture);

                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = variant;
                    var suffix = variant == ThemeVariant.Dark ? "dark" : "light";

                    Shoot(dir, $"01-start-{suffix}", ci, thumbnails, _ => { });
                    Shoot(dir, $"02-preview-{suffix}", ci, thumbnails, vm =>
                    {
                        vm.SourceFolder = source;
                        Wait(vm.ScanCompletion);
                        vm.TargetFolder = Path.Combine(DemoRoot, "Sorted");
                    });
                    Shoot(dir, $"05-pattern-error-{suffix}", ci, thumbnails, vm =>
                    {
                        vm.SourceFolder = source;
                        Wait(vm.ScanCompletion);
                        vm.TargetFolder = Path.Combine(DemoRoot, "Sorted");
                        vm.Pattern.Text = "{YYYY}-{MM}-{DD} {hour}";
                    });

                    void ArrangePreview(MainWindowViewModel vm)
                    {
                        vm.SourceFolder = source;
                        Wait(vm.ScanCompletion);
                        vm.TargetFolder = Path.Combine(DemoRoot, "Sorted");
                    }

                    Shoot(dir, $"10-update-installed-{suffix}", ci, thumbnails, ArrangePreview, update: new UpdateShot(NewerVersion: true));
                    Shoot(dir, $"11-update-portable-{suffix}", ci, thumbnails, ArrangePreview, update: new UpdateShot(NewerVersion: true, Kind: InstallKind.Portable));
                    Shoot(dir, $"12-about-update-{suffix}", ci, thumbnails, vm =>
                    {
                        vm.IsAboutOpen = true;
                        Wait(vm.Update.CheckNowCommand.ExecuteAsync(null));
                    });
                    Shoot(dir, $"13-first-start-notice-{suffix}", ci, thumbnails, ArrangePreview, update: new UpdateShot(NoticeShown: false));
                }

                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                var target = Path.Combine(DemoRoot, "Sorted");
                Directory.CreateDirectory(target);

                Shoot(dir, "03-custom-pattern-light", ci, thumbnails, vm =>
                {
                    vm.SourceFolder = source;
                    Wait(vm.ScanCompletion);
                    vm.TargetFolder = target;
                    vm.Pattern.Text = folder == "de" ? "Urlaub {D}.{M}.{YYYY} {HH}{mm}" : "Holiday {D}.{M}.{YYYY} {HH}{mm}";
                });
                Shoot(dir, "04-folders-pattern-light", ci, thumbnails, vm =>
                {
                    vm.SourceFolder = source;
                    Wait(vm.ScanCompletion);
                    vm.TargetFolder = target;
                    vm.Pattern.Text = "{YYYY}/{MM}/{YYYY}-{MM}-{DD} {name}";
                });
                Shoot(dir, "06-move-light", ci, thumbnails, vm =>
                {
                    vm.SourceFolder = source;
                    Wait(vm.ScanCompletion);
                    vm.TargetFolder = target;
                    vm.IsMoveMode = true;
                });
                Shoot(dir, "07-confirm-move-light", ci, thumbnails, vm =>
                {
                    vm.SourceFolder = source;
                    Wait(vm.ScanCompletion);
                    vm.TargetFolder = target;
                    vm.IsMoveMode = true;
                    Wait(vm.RunCommand.ExecuteAsync(null));
                });
                Shoot(dir, "08-done-light", ci, thumbnails, vm =>
                {
                    var runTarget = Path.Combine(DemoRoot, "Sorted-" + folder);
                    Directory.CreateDirectory(runTarget);
                    vm.SourceFolder = source;
                    Wait(vm.ScanCompletion);
                    vm.TargetFolder = runTarget;
                    Wait(vm.RunCommand.ExecuteAsync(null));
                });
                Shoot(dir, "09-about-light", ci, thumbnails, vm => vm.IsAboutOpen = true);
            }
        }
        finally
        {
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            Directory.Delete(DemoRoot, recursive: true);
        }
    }

    private static void Shoot(string dir, string name, CultureInfo culture, ThumbnailLoader thumbnails, Action<MainWindowViewModel> arrange, bool scrollLeftToEnd = false, UpdateShot? update = null)
    {
        update ??= new UpdateShot();
        var running = RunningVersion.Parsed ?? AppVersion.Parse("0.1.0");
        var newer = AppVersion.Parse($"{running.Major}.{running.Minor + 1}.0");
        var latest = new ReleaseInfo(
            update.NewerVersion ? newer : running,
            new Uri("https://github.com/jannikjordan/snapdate/releases/latest"),
            false,
            false,
            []);
        var settings = new MemorySettings(new AppSettings { UpdateNoticeShown = update.NoticeShown });
        var services = new AppServices(
            new FixedTheme(),
            new NoDesktop(),
            settings,
            new LocalFileSystem(),
            new MetadataExtractorReader(TimeProvider.System),
            TimeProvider.System,
            thumbnails,
            new UpdateCheckService(new FixedFeed(latest), settings, new FixedClock(), running),
            new FixedInstall(update.Kind));
        var vm = new MainWindowViewModel(services, culture);
        Wait(vm.Update.AutomaticCheckCompletion);
        var window = new MainWindow { DataContext = vm, Width = _width, Height = _height };
        window.Show();
        arrange(vm);

        Dispatcher.UIThread.RunJobs();
        if (scrollLeftToEnd)
        {
            window.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
        }

        // Let layout settle and lazily loaded thumbnails arrive.
        for (var i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }

        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        var path = Path.Combine(dir, name + ".png");
        frame.Save(path, PngBitmapEncoderOptions.Default);
        window.Close();
        Console.WriteLine($"wrote {Path.GetFileName(dir)}/{name}.png");
    }

    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for the view model.");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
    }
}
