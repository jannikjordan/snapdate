using System.Globalization;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.Application.FileNames;
using Snapdate.Application.Installation;
using Snapdate.Application.Ports;
using Snapdate.Application.Settings;
using Snapdate.Application.Updates;
using Snapdate.Domain.FileNames;
using Snapdate.Domain.Versioning;
using Snapdate.Infrastructure.FileNames;
using Snapdate.Infrastructure.FileSystem;
using Snapdate.Infrastructure.Metadata;

namespace Snapdate.App.Tests.Support;

internal sealed class FakeTheme : IThemeService
{
    public bool IsDark { get; private set; }

    public int Toggles { get; private set; }

    public string? CurrentChoice { get; private set; }

    public void Toggle()
    {
        IsDark = !IsDark;
        CurrentChoice = IsDark ? "dark" : "light";
        Toggles++;
    }

    public void Apply(string? theme)
    {
        CurrentChoice = theme;
        IsDark = theme == "dark";
    }
}

internal sealed class FakeDesktop : IDesktopServices
{
    public List<Uri> OpenedUrls { get; } = [];

    public string? NextPickedFolder { get; set; }

    public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult(NextPickedFolder);

    public Task OpenUrlAsync(Uri url)
    {
        OpenedUrls.Add(url);
        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string path) => Task.CompletedTask;

    public Task OpenFileAsync(string path) => Task.CompletedTask;
}

internal sealed class MemorySettings(AppSettings? initial = null) : ISettingsStore
{
    public AppSettings Current { get; private set; } = initial ?? new AppSettings();

    public AppSettings Load() => Current;

    public int Saves { get; private set; }

    public void Save(AppSettings settings)
    {
        Saves++;
        Current = settings;
    }
}

internal sealed class TestServices
{
    public FakeTheme Theme { get; } = new();

    public FakeDesktop Desktop { get; } = new();

    public MemorySettings Settings { get; init; } = new();

    /// <summary>When set, the update check is wired to this feed (never the network).</summary>
    public FakeFeed? Feed { get; init; }

    public FakeClock Clock { get; } = new();

    public InstallKind InstallKind { get; init; } = InstallKind.Installed;

    public AppVersion CurrentVersion { get; init; } = AppVersion.Parse("1.0.0");

    public FakeUpdateInstaller Installer { get; } = new();

    public RecordingAppExit AppExit { get; } = new();

    /// <summary>The built-in rules and no user file; never touches %APPDATA%.</summary>
    public MemoryDateRuleStore? DateRules { get; init; } = new();

    public MainWindowViewModel CreateViewModel(string culture = "en-US") =>
        new(
            new AppServices(
                Theme,
                Desktop,
                Settings,
                new LocalFileSystem(),
                new MetadataExtractorReader(TimeProvider.System),
                TimeProvider.System,
                null,
                Feed is null ? null : new UpdateCheckService(Feed, Settings, Clock, CurrentVersion),
                new FakeInstall(InstallKind),
                Installer,
                AppExit,
                DateRules),
            CultureInfo.GetCultureInfo(culture));
}

/// <summary>An in-memory rule store: the embedded built-in rules (or the given ones) and an in-memory user file.</summary>
internal sealed class MemoryDateRuleStore(IReadOnlyList<DateRule>? systemRules = null, UserDateRules? userRules = null) : IDateRuleStore
{
    // Only the embedded resource is read; the path is never used.
    private static readonly IReadOnlyList<DateRule> BuiltIn =
        new JsonDateRuleStore(Path.Combine(Path.GetTempPath(), "snapdate-app-tests-unused.json")).LoadSystemRules();

    private readonly IReadOnlyList<DateRule> _system = systemRules ?? BuiltIn;

    public UserDateRules? User { get; private set; } = userRules;

    public IReadOnlyList<DateRule> LoadSystemRules() => _system;

    public UserDateRulesLoad LoadUserRules() =>
        User is null ? UserDateRulesLoad.Missing : new UserDateRulesLoad(User, UserRulesStatus.Ok, Fingerprint(User));

    public UserDateRulesSave SaveUserRules(UserDateRules rules, UserRulesFingerprint expected)
    {
        if (!expected.Matches(LoadUserRules().Fingerprint))
        {
            return new UserDateRulesSave(DateRuleSaveResult.Conflict, expected);
        }

        User = rules;
        return new UserDateRulesSave(DateRuleSaveResult.Saved, Fingerprint(rules));
    }

    private static UserRulesFingerprint Fingerprint(UserDateRules rules) =>
        UserRulesFingerprint.ForContent(rules.CustomRules.Count, rules.GetHashCode().ToString("X8", CultureInfo.InvariantCulture));
}

internal sealed class FakeFeed : IReleaseFeed
{
    public ReleaseInfo? Latest { get; set; }

    public int Calls { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        return Latest;
    }

    public static ReleaseInfo Release(string version) =>
        new(AppVersion.Parse(version), new Uri($"https://github.com/jannikjordan/snapdate/releases/tag/v{version}"), false, false, []);
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeInstall(InstallKind kind) : IInstallInfo
{
    public InstallInfo Current { get; } = new(kind, kind == InstallKind.Installed ? InstallScope.CurrentUser : InstallScope.None);
}

/// <summary>A scripted in-app updater: reports the given steps, then waits for <see cref="Gate"/> (if any) and returns <see cref="Result"/>.</summary>
internal sealed class FakeUpdateInstaller : IUpdateInstaller
{
    public bool CanInstall { get; set; } = true;

    public UpdateInstallResult Result { get; set; } = UpdateInstallResult.Completed;

    public List<ReleaseInfo> Started { get; } = [];

    /// <summary>Reports happen first, in order, before the gate.</summary>
    public List<UpdateProgress> Progress { get; } = [];

    public List<UpdatePhase> Phases { get; } = [];

    public TaskCompletionSource? Gate { get; set; }

    public Exception? Throw { get; set; }

    public CancellationToken SeenCancellation { get; private set; }

    public CancellationToken SeenShutdown { get; private set; }

    public bool CanInstallInApp(ReleaseInfo release) => CanInstall;

    public async Task<UpdateInstallResult> InstallAsync(
        ReleaseInfo release,
        IProgress<UpdateProgress> progress,
        IProgress<UpdatePhase> phase,
        CancellationToken cancellationToken,
        CancellationToken appShutdown)
    {
        Started.Add(release);
        SeenCancellation = cancellationToken;
        SeenShutdown = appShutdown;
        foreach (var p in Phases.Where(p => p == UpdatePhase.Downloading))
        {
            phase.Report(p);
        }

        foreach (var p in Progress)
        {
            progress.Report(p);
        }

        foreach (var p in Phases.Where(p => p != UpdatePhase.Downloading))
        {
            phase.Report(p);
        }

        if (Throw is not null)
        {
            throw Throw;
        }

        if (Gate is not null)
        {
            using var both = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, appShutdown);
            try
            {
                await Gate.Task.WaitAsync(both.Token);
            }
            catch (OperationCanceledException)
            {
                // As the real use case: a shutdown after the installer started ends the wait with InstallerRunning.
                return appShutdown.IsCancellationRequested && Phases.Contains(UpdatePhase.Installing)
                    ? UpdateInstallResult.InstallerRunning
                    : UpdateInstallResult.Cancelled;
            }
        }

        return Result;
    }
}

internal sealed class RecordingAppExit : IAppExit
{
    public int Exits { get; private set; }

    public void CloseApp() => Exits++;
}

/// <summary>A unique temporary folder that is deleted after the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snapdate-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Creates a file without metadata; its file date becomes the capture date.</summary>
    public string Photo(string relative, DateTime fileDate)
    {
        var full = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, [0xFF, 0xD8, 0xFF, 0xD9]);
        File.SetCreationTime(full, fileDate);
        File.SetLastWriteTime(full, fileDate);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
