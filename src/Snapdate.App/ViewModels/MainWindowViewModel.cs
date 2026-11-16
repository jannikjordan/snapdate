using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snapdate.App.Localization;
using Snapdate.App.Services;
using Snapdate.Application.Execution;
using Snapdate.Application.Localization;
using Snapdate.Application.Planning;
using Snapdate.Application.Scanning;
using Snapdate.Application.Settings;
using Snapdate.Domain.Planning;

namespace Snapdate.App.ViewModels;

public enum Phase
{
    /// <summary>Folders not (fully) chosen or nothing found yet.</summary>
    Setup,
    Scanning,
    Ready,
    Running,
    Done,
}

public enum PreviewFilter
{
    All,
    Camera,
    FileDate,
    Renamed,
}

public enum StatusKind
{
    Info,
    Warning,
    Ready,
    Success,
}

/// <summary>State and behaviour of the main window: folders → name → preview → run.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ScanSourceUseCase _scan;
    private readonly BuildPlanUseCase _planner;
    private readonly ExecutePlanUseCase _executor;

    private List<ScannedPhoto> _photos = [];
    private RenamePlan? _plan;
    private List<PreviewItemViewModel> _allItems = [];
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _runCts;
    private ExecutionResult? _result;
    private bool _languageChosenByUser;
    private bool _initializing = true;

    public MainWindowViewModel(AppServices services, CultureInfo systemUiCulture)
    {
        _services = services;
        _scan = new ScanSourceUseCase(services.FileSystem, services.MetadataReader);
        _planner = new BuildPlanUseCase(services.FileSystem);
        _executor = new ExecutePlanUseCase(services.FileSystem, services.Clock);

        var settings = services.Settings.Load();
        _languageChosenByUser = settings.Language is not null;
        Language = UiLanguageResolver.Resolve(systemUiCulture, settings.Language);
        L = CreateStrings(Language);
        services.Theme.Apply(settings.Theme);

        Pattern = new PatternBuilderViewModel(L, settings.Pattern);
        Pattern.PatternChanged += (_, _) => Replan();
        IncludeSubfolders = settings.IncludeSubfolders;
        IsCopyMode = settings.Mode == TransferMode.Copy;
        SourceFolder = ExistingOrNull(settings.SourceFolder);
        TargetFolder = ExistingOrNull(settings.TargetFolder);

        _initializing = false;
        if (SourceFolder is not null)
        {
            StartScan();
        }
        else
        {
            Replan();
        }
    }

    /// <summary>Completes when the most recent folder scan has finished (used by tests and tools).</summary>
    public Task ScanCompletion { get; private set; } = Task.CompletedTask;

    // ---------- Language & theme ----------

    /// <summary>All UI strings for the current language.</summary>
    [ObservableProperty]
    public partial LocalizedStrings L { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnglish), nameof(IsGerman))]
    public partial UiLanguage Language { get; set; }

    public bool IsEnglish
    {
        get => Language == UiLanguage.English;
        set
        {
            if (value)
            {
                _languageChosenByUser = true;
                Language = UiLanguage.English;
            }
        }
    }

    public bool IsGerman
    {
        get => Language == UiLanguage.German;
        set
        {
            if (value)
            {
                _languageChosenByUser = true;
                Language = UiLanguage.German;
            }
        }
    }

    public string VersionLabel { get; } = "v" + GetVersion();

    // ---------- Step 1: folders ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceDisplay), nameof(HasSource))]
    public partial string? SourceFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetDisplay), nameof(HasTarget))]
    public partial string? TargetFolder { get; set; }

    public bool HasSource => SourceFolder is not null;

    public bool HasTarget => TargetFolder is not null;

    public string SourceDisplay => SourceFolder is null ? L.NotSelected : ShortenPath(SourceFolder);

    public string TargetDisplay => TargetFolder is null ? L.NotSelected : ShortenPath(TargetFolder);

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = true;

    // ---------- Step 2: pattern ----------

    public PatternBuilderViewModel Pattern { get; }

    [ObservableProperty]
    public partial string ExampleName { get; private set; } = string.Empty;

    // ---------- Step 3: mode ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMoveMode))]
    public partial bool IsCopyMode { get; set; } = true;

    public bool IsMoveMode
    {
        get => !IsCopyMode;
        set => IsCopyMode = !value;
    }

    private TransferMode Mode => IsCopyMode ? TransferMode.Copy : TransferMode.Move;

    // ---------- Preview ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScanning), nameof(IsRunning), nameof(IsDone), nameof(IsIdle), nameof(CanEdit))]
    public partial Phase Phase { get; private set; } = Phase.Setup;

    public bool IsScanning => Phase == Phase.Scanning;

    public bool IsRunning => Phase == Phase.Running;

    public bool IsDone => Phase == Phase.Done;

    public bool IsIdle => Phase is Phase.Setup or Phase.Scanning or Phase.Ready;

    /// <summary>Inputs are locked while files are being copied.</summary>
    public bool CanEdit => Phase != Phase.Running;

    public bool ShowEmpty => SourceFolder is null && Phase != Phase.Scanning;

    public bool ShowScanning => Phase == Phase.Scanning;

    public bool ShowNoPhotos => SourceFolder is not null && Phase != Phase.Scanning && _photos.Count == 0;

    public bool ShowList => Phase != Phase.Scanning && _photos.Count > 0;

    [ObservableProperty]
    public partial int ScanCount { get; private set; }

    public string ScanCountText => L.Format(nameof(LocalizedStrings.ScanningBody), ScanCount);

    [ObservableProperty]
    public partial IReadOnlyList<PreviewItemViewModel> VisibleItems { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterAll), nameof(IsFilterCamera), nameof(IsFilterFile), nameof(IsFilterRenamed))]
    public partial PreviewFilter Filter { get; set; } = PreviewFilter.All;

    public bool IsFilterAll
    {
        get => Filter == PreviewFilter.All;
        set { if (value) { Filter = PreviewFilter.All; } }
    }

    public bool IsFilterCamera
    {
        get => Filter == PreviewFilter.Camera;
        set { if (value) { Filter = PreviewFilter.Camera; } }
    }

    public bool IsFilterFile
    {
        get => Filter == PreviewFilter.FileDate;
        set { if (value) { Filter = PreviewFilter.FileDate; } }
    }

    public bool IsFilterRenamed
    {
        get => Filter == PreviewFilter.Renamed;
        set { if (value) { Filter = PreviewFilter.Renamed; } }
    }

    [ObservableProperty]
    public partial int CountAll { get; private set; }

    [ObservableProperty]
    public partial int CountCamera { get; private set; }

    [ObservableProperty]
    public partial int CountFile { get; private set; }

    [ObservableProperty]
    public partial int CountRenamed { get; private set; }

    public string PhotosCountText => L.Plural(nameof(LocalizedStrings.PhotosCount), CountAll);

    // ---------- Action bar ----------

    [ObservableProperty]
    public partial string StatusTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusSubtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusWarning), nameof(IsStatusReady), nameof(IsStatusInfo))]
    public partial StatusKind StatusKind { get; private set; }

    public bool IsStatusWarning => StatusKind == StatusKind.Warning;

    public bool IsStatusReady => StatusKind is StatusKind.Ready or StatusKind.Success;

    public bool IsStatusInfo => StatusKind == StatusKind.Info;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    public partial bool CanRun { get; private set; }

    [ObservableProperty]
    public partial string RunButtonText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RunningTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DoneTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DoneSubtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool DoneHasProblems { get; private set; }

    public bool HasLog => _result?.LogFilePath is not null;

    // ---------- Overlays ----------

    [ObservableProperty]
    public partial bool IsAboutOpen { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmMoveOpen { get; private set; }

    [ObservableProperty]
    public partial string ConfirmMoveText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmMoveYesText { get; private set; } = string.Empty;

    public string AboutVersionText => L.Format(nameof(LocalizedStrings.AboutVersion), GetVersion());

    // ---------- Commands ----------

    [RelayCommand]
    private void ToggleTheme() => _services.Theme.Toggle();

    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var path = await _services.Desktop.PickFolderAsync(L.PickSourceTitle, SourceFolder).ConfigureAwait(true);
        if (path is not null)
        {
            SourceFolder = path;
        }
    }

    [RelayCommand]
    private async Task PickTargetAsync()
    {
        var path = await _services.Desktop.PickFolderAsync(L.PickTargetTitle, TargetFolder ?? SourceFolder).ConfigureAwait(true);
        if (path is not null)
        {
            TargetFolder = path;
        }
    }

    /// <summary>Called by the view when a folder is dropped on one of the folder boxes.</summary>
    public void DropFolder(string path, bool asSource)
    {
        if (!CanEdit)
        {
            return;
        }

        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        if (asSource)
        {
            SourceFolder = folder;
        }
        else
        {
            TargetFolder = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        // Re-plan right before running so files that appeared meanwhile are respected.
        Replan();
        if (!CanRun || _plan is null)
        {
            return;
        }

        if (Mode == TransferMode.Move)
        {
            ConfirmMoveText = L.Plural(nameof(LocalizedStrings.ConfirmMoveBody), _plan.Items.Count);
            ConfirmMoveYesText = L.Plural(nameof(LocalizedStrings.ConfirmMoveYes), _plan.Items.Count);
            IsConfirmMoveOpen = true;
            return;
        }

        await ExecuteAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ConfirmMoveAsync()
    {
        IsConfirmMoveOpen = false;
        await ExecuteAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelMove() => IsConfirmMoveOpen = false;

    [RelayCommand]
    private void Cancel() => _runCts?.Cancel();

    [RelayCommand]
    private Task OpenTargetFolderAsync() =>
        TargetFolder is null ? Task.CompletedTask : _services.Desktop.OpenFolderAsync(TargetFolder);

    [RelayCommand]
    private Task ShowLogAsync() =>
        _result?.LogFilePath is { } log ? _services.Desktop.OpenFileAsync(log) : Task.CompletedTask;

    [RelayCommand]
    private void StartOver()
    {
        _result = null;
        OnPropertyChanged(nameof(HasLog));
        Phase = Phase.Setup;
        StartScan();
    }

    [RelayCommand]
    private Task OpenHelpAsync() => _services.Desktop.OpenUrlAsync(AppLinks.UserGuide(Language));

    [RelayCommand]
    private Task OpenWebsiteAsync() => _services.Desktop.OpenUrlAsync(AppLinks.Website);

    [RelayCommand]
    private void ToggleAbout() => IsAboutOpen = !IsAboutOpen;

    /// <summary>Persists the current choices. Called when running and when the window closes.</summary>
    public void SaveSettings() =>
        _services.Settings.Save(new AppSettings
        {
            SourceFolder = SourceFolder,
            TargetFolder = TargetFolder,
            Pattern = Pattern.Pattern?.Text ?? Pattern.Text,
            IncludeSubfolders = IncludeSubfolders,
            Mode = Mode,
            Language = _languageChosenByUser ? Language : null,
            Theme = _services.Theme.CurrentChoice,
        });

    // ---------- Change handlers ----------

    partial void OnLanguageChanged(UiLanguage value)
    {
        L = CreateStrings(value);
        if (_initializing)
        {
            return;
        }

        Pattern.UpdateStrings(L);
        OnPropertyChanged(nameof(SourceDisplay));
        OnPropertyChanged(nameof(TargetDisplay));
        OnPropertyChanged(nameof(ScanCountText));
        OnPropertyChanged(nameof(PhotosCountText));
        OnPropertyChanged(nameof(AboutVersionText));

        if (Phase == Phase.Done && _result is not null)
        {
            ShowResult(_result);
            RunningTitle = Mode == TransferMode.Copy ? L.RunningCopy : L.RunningMove;
        }
        else
        {
            Replan();
        }
    }

    partial void OnSourceFolderChanged(string? value)
    {
        if (!_initializing)
        {
            StartScan();
        }
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (!_initializing)
        {
            StartScan();
        }
    }

    partial void OnTargetFolderChanged(string? value) => Replan();

    partial void OnIsCopyModeChanged(bool value) => Replan();

    partial void OnFilterChanged(PreviewFilter value) => ApplyFilter();

    partial void OnScanCountChanged(int value)
    {
        OnPropertyChanged(nameof(ScanCountText));
        if (Phase == Phase.Scanning)
        {
            StatusSubtitle = ScanCountText;
        }
    }

    partial void OnCountAllChanged(int value) => OnPropertyChanged(nameof(PhotosCountText));

    // ---------- Workflow ----------

    private void StartScan() => ScanCompletion = ScanAsync();

    private async Task ScanAsync()
    {
        if (_scanCts is not null)
        {
            await _scanCts.CancelAsync().ConfigureAwait(true);
            _scanCts.Dispose();
            _scanCts = null;
        }

        _photos = [];
        _result = null;

        var problem = FolderCheck.Check(_services.FileSystem, SourceFolder, TargetFolder, IncludeSubfolders);
        if (SourceFolder is null || problem is FolderProblem.SourceMissing or FolderProblem.SourceNotFound)
        {
            Phase = Phase.Setup;
            Replan();
            return;
        }

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        Phase = Phase.Scanning;
        ScanCount = 0;
        RefreshPreviewState();
        UpdateStatus();

        var source = SourceFolder;
        var recursive = IncludeSubfolders;
        IProgress<int> progress = new Progress<int>(count =>
        {
            if (!cts.IsCancellationRequested)
            {
                ScanCount = count;
            }
        });

        try
        {
            var photos = await Task.Run(
                async () =>
                {
                    var list = new List<ScannedPhoto>();
                    await foreach (var photo in _scan.ScanAsync(source, recursive, cts.Token).ConfigureAwait(false))
                    {
                        list.Add(photo);
                        if (list.Count % 25 == 0)
                        {
                            progress.Report(list.Count);
                        }
                    }

                    return list;
                },
                cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            _photos = photos;
            ScanCount = photos.Count;
            Phase = Phase.Setup;
            Replan();
        }
        catch (OperationCanceledException)
        {
            // A newer scan replaced this one.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _photos = [];
            Phase = Phase.Setup;
            Replan();
        }
    }

    private void Replan()
    {
        if (_initializing || Phase is Phase.Running or Phase.Scanning)
        {
            UpdateStatus();
            return;
        }

        if (Phase == Phase.Done)
        {
            // Inputs changed after a run: go back to the preview.
            Phase = Phase.Setup;
        }

        var pattern = Pattern.Pattern;
        if (pattern is not null && SourceFolder is not null && _photos.Count > 0)
        {
            var problem = FolderCheck.Check(_services.FileSystem, SourceFolder, TargetFolder, IncludeSubfolders);
            var target = problem == FolderProblem.None ? TargetFolder : null;
            _plan = _planner.Build(_photos, pattern, L.Culture, SourceFolder, target, Mode);
            _allItems = _plan.Items.Select(i => new PreviewItemViewModel(i, L, _services.Thumbnails)).ToList();
            ExampleName = _allItems[0].NewName;
        }
        else
        {
            _plan = null;
            if (pattern is not null)
            {
                _allItems = [];
                ExampleName = pattern.Render(PatternBuilderViewModel.SampleFacts, L.Culture).Replace('/', '\\') + ".jpg";
            }
        }

        CountAll = _allItems.Count;
        CountCamera = _allItems.Count(i => i.Item.Photo.Date.IsFromMetadata);
        CountFile = CountAll - CountCamera;
        CountRenamed = _allItems.Count(i => i.Item.WasRenamed);
        ApplyFilter();
        RefreshPreviewState();
        UpdateStatus();
    }

    private void ApplyFilter() =>
        VisibleItems = Filter switch
        {
            PreviewFilter.Camera => _allItems.Where(i => i.Item.Photo.Date.IsFromMetadata).ToList(),
            PreviewFilter.FileDate => _allItems.Where(i => !i.Item.Photo.Date.IsFromMetadata).ToList(),
            PreviewFilter.Renamed => _allItems.Where(i => i.Item.WasRenamed).ToList(),
            _ => _allItems,
        };

    private void RefreshPreviewState()
    {
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowScanning));
        OnPropertyChanged(nameof(ShowNoPhotos));
        OnPropertyChanged(nameof(ShowList));
    }

    private void UpdateStatus()
    {
        if (Phase is Phase.Running or Phase.Done)
        {
            CanRun = false;
            return;
        }

        var problem = FolderCheck.Check(_services.FileSystem, SourceFolder, TargetFolder, IncludeSubfolders);
        var count = _plan?.Items.Count ?? _photos.Count;
        var runKey = IsCopyMode ? nameof(LocalizedStrings.RunCopy) : nameof(LocalizedStrings.RunMove);
        RunButtonText = count == 0 ? L.Get(runKey + "None") : L.Plural(runKey, count);

        var (kind, title, sub) = (problem, Phase) switch
        {
            (FolderProblem.SourceMissing, _) => (StatusKind.Info, L.ProblemSourceMissing, L.StatusNothingChanged),
            (FolderProblem.SourceNotFound, _) => (StatusKind.Warning, L.ProblemSourceNotFound, L.StatusNothingChanged),
            (_, Phase.Scanning) => (StatusKind.Info, L.ScanningTitle, ScanCountText),
            (FolderProblem.TargetMissing, _) => (StatusKind.Info, L.ProblemTargetMissing, L.StatusNothingChanged),
            (FolderProblem.TargetInsideSource, _) => (StatusKind.Warning, L.ProblemTargetInsideSource, L.ProblemTargetInsideSourceHint),
            (FolderProblem.SourceInsideTarget, _) => (StatusKind.Warning, L.ProblemSourceInsideTarget, L.ProblemSourceInsideTargetHint),
            _ when Pattern.Pattern is null => (StatusKind.Warning, L.ProblemPattern, L.ProblemPatternHint),
            _ when count == 0 => (StatusKind.Info, L.ProblemNoPhotos, L.StatusNothingChanged),
            _ => (StatusKind.Ready, L.Plural(nameof(LocalizedStrings.ReadyTitle), count), IsCopyMode ? L.ReadySubCopy : L.ReadySubMove),
        };

        StatusKind = kind;
        StatusTitle = title;
        StatusSubtitle = sub;
        CanRun = kind == StatusKind.Ready && _plan is { CanExecute: true };
        if (Phase != Phase.Scanning)
        {
            Phase = CanRun ? Phase.Ready : Phase.Setup;
        }
    }

    private async Task ExecuteAsync()
    {
        if (_plan is not { CanExecute: true } plan)
        {
            return;
        }

        SaveSettings();
        var cts = new CancellationTokenSource();
        _runCts = cts;
        Phase = Phase.Running;
        CanRun = false;
        RunningTitle = plan.Mode == TransferMode.Copy ? L.RunningCopy : L.RunningMove;
        Progress = 0;
        ProgressText = $"0 / {plan.Items.Count}";

        IProgress<ExecutionProgress> progress = new Progress<ExecutionProgress>(p =>
        {
            Progress = p.Total == 0 ? 1 : (double)p.Completed / p.Total;
            ProgressText = $"{p.Completed} / {p.Total}";
        });

        ExecutionResult result;
        try
        {
            result = await Task.Run(() => _executor.ExecuteAsync(plan, progress, cts.Token)).ConfigureAwait(true);
        }
        finally
        {
            _runCts = null;
            cts.Dispose();
        }

        _result = result;
        ShowResult(result);
        Phase = Phase.Done;
        StatusKind = result.FailedCount > 0 || result.WasCancelled ? StatusKind.Warning : StatusKind.Success;
        OnPropertyChanged(nameof(HasLog));
    }

    private void ShowResult(ExecutionResult result)
    {
        var mode = _plan?.Mode ?? Mode;
        DoneTitle = L.Plural(
            mode == TransferMode.Copy ? nameof(LocalizedStrings.DoneCopy) : nameof(LocalizedStrings.DoneMove),
            result.DoneCount + result.UnchangedCount);
        DoneHasProblems = result.FailedCount > 0 || result.WasCancelled;
        DoneSubtitle = result.WasCancelled
            ? L.DoneCancelled
            : result.FailedCount > 0
                ? L.Plural(nameof(LocalizedStrings.DoneFailed), result.FailedCount)
                : L.DoneSub;
    }

    private static string? ExistingOrNull(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? folder : null;

    /// <summary>Shortens long paths in the middle: <c>D:\Pictures\…\Summer 2024</c>.</summary>
    internal static string ShortenPath(string path, int max = 44)
    {
        if (path.Length <= max)
        {
            return path;
        }

        var root = Path.GetPathRoot(path) ?? string.Empty;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        var first = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var shortened = $"{root}{first}{Path.DirectorySeparatorChar}…{Path.DirectorySeparatorChar}{name}";
        return shortened.Length <= max ? shortened : $"…{Path.DirectorySeparatorChar}{name}";
    }

    private static LocalizedStrings CreateStrings(UiLanguage language) =>
        new(UiLanguageResolver.ToCulture(language));

    private static string GetVersion()
    {
        var info = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? info[..plus] : info;
    }
}
