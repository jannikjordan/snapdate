using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snapdate.App.Localization;
using Snapdate.App.Services;
using Snapdate.Application.Installation;
using Snapdate.Application.Updates;

namespace Snapdate.App.ViewModels;

/// <summary>What the last manual "Check now" found (the text is built from this, so it follows the language).</summary>
public enum UpdateCheckOutcome
{
    None,
    Checking,
    UpToDate,
    Available,
    Failed,
}

/// <summary>
/// The update check as the user sees it: the start-up check, the banner, the about-panel controls and
/// the one-time notice. Never blocks start-up and never shows an error for the automatic check.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly UpdateCheckService? _check;
    private readonly InstallInfo _install;
    private readonly IUpdateInstaller? _installer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _installCancel;
    private CancellationTokenSource? _installShutdown;
    private UpdateInstallState _state;
    private UpdateFailure _failure;
    private double _progress;
    private bool _disposed;
    private ReleaseInfo? _release;
    private UpdateCheckOutcome _outcome;
    private LocalizedStrings _l;

    public UpdateViewModel(AppServices services, LocalizedStrings strings)
    {
        _services = services;
        _l = strings;
        _check = services.UpdateCheck;
        _install = services.Install?.Current ?? InstallInfo.Portable;
        _installer = services.UpdateInstaller;

        var settings = services.Settings.Load();
        AutoCheck = settings.AutoUpdateCheck;
        _initializing = false;

        if (_check is not null && AutoCheck)
        {
            if (!settings.UpdateNoticeShown)
            {
                // Users are told before the first request: this session only shows the notice (remembered
                // right away, so it appears exactly once); the check itself starts with the next start.
                services.Settings.Save(services.Settings.Load() with { UpdateNoticeShown = true });
                IsNoticeVisible = true;
            }
            else
            {
                AutomaticCheckCompletion = RunAutomaticCheckAsync();
            }
        }
    }

    private bool _initializing = true;

    /// <summary>Completes when the start-up check has finished (used by tests and tools).</summary>
    public Task AutomaticCheckCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>False when the running version is unknown: the whole feature is then hidden.</summary>
    public bool IsAvailable => _check is not null;

    public bool IsInstalled => _install.Kind == InstallKind.Installed;

    public bool IsPortable => !IsInstalled;

    // ---------- Setting ----------

    [ObservableProperty]
    public partial bool AutoCheck { get; set; }

    partial void OnAutoCheckChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        _services.Settings.Save(_services.Settings.Load() with { AutoUpdateCheck = value });
        if (!value)
        {
            IsNoticeVisible = false;
        }
    }

    // ---------- Banner ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerTitle), nameof(BannerSubtitle), nameof(BannerAnnouncement))]
    public partial bool IsBannerVisible { get; private set; }

    public string BannerTitle => _release is null ? string.Empty : _l.Format(nameof(LocalizedStrings.UpdateAvailableTitle), _release.Version);

    public string BannerSubtitle => _l.Format(nameof(LocalizedStrings.UpdateAvailableBody), RunningVersion.Text);

    public string BannerAnnouncement => _state switch
    {
        UpdateInstallState.Idle => $"{BannerTitle}. {BannerSubtitle}",
        UpdateInstallState.Failed => $"{_l.UpdateFailedTitle}. {FailureText}",
        _ => StatusText,
    };

    // ---------- In-app update ----------

    public UpdateInstallState InstallState => _state;

    /// <summary>The normal offer (title, Update now, What's new, Later, Skip).</summary>
    public bool IsOffering => _state == UpdateInstallState.Idle;

    public bool IsBusy => _state is UpdateInstallState.Downloading or UpdateInstallState.Verifying or UpdateInstallState.Installing;

    public bool IsDownloading => _state == UpdateInstallState.Downloading;

    public bool IsFailed => _state == UpdateInstallState.Failed;

    /// <summary>Only the download can be cancelled; once the verified installer runs the user answers Windows' prompt.</summary>
    public bool CanCancel => IsDownloading;

    /// <summary>Download progress from 0 to 1.</summary>
    public double Progress => _progress;

    public string ProgressText => _l.Format(nameof(LocalizedStrings.UpdateProgressPercent), (int)Math.Floor(_progress * 100));

    public string StatusText => _state switch
    {
        UpdateInstallState.Downloading => _l.UpdateDownloading,
        UpdateInstallState.Verifying => _l.UpdateVerifying,
        UpdateInstallState.Installing => _l.UpdateInstalling,
        _ => string.Empty,
    };

    public UpdateFailure Failure => _failure;

    /// <summary>"Try again" makes sense when the failure may be temporary or the user declined a prompt.</summary>
    public bool CanRetry => _failure is UpdateFailure.Download or UpdateFailure.Start or UpdateFailure.Installer;

    public string FailureText => _failure switch
    {
        UpdateFailure.Download => _l.UpdateFailedDownload,
        UpdateFailure.Verification => _l.UpdateFailedVerify,
        UpdateFailure.MissingInstaller => _l.UpdateFailedMissing,
        UpdateFailure.Start => _l.UpdateFailedStart,
        UpdateFailure.Installer => _l.UpdateFailedInstaller,
        _ => string.Empty,
    };

    private void SetState(UpdateInstallState state, UpdateFailure failure = UpdateFailure.None)
    {
        _state = state;
        _failure = failure;
        foreach (var name in new[]
        {
            nameof(InstallState), nameof(IsOffering), nameof(IsBusy), nameof(IsDownloading), nameof(IsFailed), nameof(CanCancel),
            nameof(StatusText), nameof(Failure), nameof(CanRetry), nameof(FailureText), nameof(BannerAnnouncement),
        })
        {
            OnPropertyChanged(name);
        }

        CancelUpdateCommand.NotifyCanExecuteChanged();
        LaterCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
    }

    private void OnProgress(UpdateProgress p)
    {
        if (!IsDownloading || p.TotalBytes <= 0)
        {
            return;
        }

        _progress = Math.Clamp((double)p.BytesReceived / p.TotalBytes, 0, 1);
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void OnPhase(UpdatePhase phase)
    {
        if (!IsBusy)
        {
            return;
        }

        SetState(phase switch
        {
            UpdatePhase.Verifying => UpdateInstallState.Verifying,
            UpdatePhase.Installing => UpdateInstallState.Installing,
            _ => UpdateInstallState.Downloading,
        });
    }

    // ---------- One-time notice ----------

    [ObservableProperty]
    public partial bool IsNoticeVisible { get; private set; }

    // ---------- Manual check ----------

    public bool IsChecking => _outcome == UpdateCheckOutcome.Checking;

    public bool HasCheckResult => _outcome != UpdateCheckOutcome.None;

    public UpdateCheckOutcome Outcome => _outcome;

    public string CheckResultText => _outcome switch
    {
        UpdateCheckOutcome.Checking => _l.UpdateChecking,
        UpdateCheckOutcome.UpToDate => _l.Format(nameof(LocalizedStrings.UpdateUpToDate), RunningVersion.Text),
        UpdateCheckOutcome.Available => _release is null ? string.Empty : _l.Format(nameof(LocalizedStrings.UpdateAvailableTitle), _release.Version),
        UpdateCheckOutcome.Failed => _l.UpdateCheckFailed,
        _ => string.Empty,
    };

    public bool IsCheckFailed => _outcome == UpdateCheckOutcome.Failed;

    // ---------- Commands ----------

    /// <summary>
    /// "Update now" (installed builds): runs the verified in-app update with progress. When the in-app path does not
    /// apply the release page is opened instead. After a started installer the app never exits by itself, except on
    /// <see cref="UpdateInstallResult.Completed"/>: the new version is then already running (Setup was started with
    /// <c>/RESTARTAPP</c>), so the app exits immediately.
    /// </summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_release is not { } release || IsBusy)
        {
            return;
        }

        if (_installer is null || !_installer.CanInstallInApp(release))
        {
            await _services.Desktop.OpenUrlAsync(release.PageUrl).ConfigureAwait(true);
            return;
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        using var shutdown = new CancellationTokenSource();
        _installCancel = cancel;
        _installShutdown = shutdown;
        _progress = 0;
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        SetState(UpdateInstallState.Downloading);

        UpdateInstallResult result;
        try
        {
            result = await _installer.InstallAsync(
                release,
                new ContextProgress<UpdateProgress>(OnProgress),
                new ContextProgress<UpdatePhase>(OnPhase),
                cancel.Token,
                shutdown.Token).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // The installer must not throw; if it does, the user gets the "failed" message, not a crash.
        catch (Exception)
#pragma warning restore CA1031
        {
            result = UpdateInstallResult.DownloadFailed;
        }
        finally
        {
            _installCancel = null;
            _installShutdown = null;
        }

        if (_disposed)
        {
            return;
        }

        switch (result)
        {
            case UpdateInstallResult.Completed:
                _services.AppExit?.CloseApp();
                break;
            case UpdateInstallResult.InstallerRunning:
                // Normally the app is being closed and nobody sees this. If it is still alive (for example a sign-out was
                // cancelled) the banner goes back to normal, hidden, so it does not offer a second installer run;
                // later update checks work as before.
                IsBannerVisible = false;
                SetState(UpdateInstallState.Idle);
                break;
            case UpdateInstallResult.Cancelled:
                SetState(UpdateInstallState.Idle);
                break;
            case UpdateInstallResult.NotApplicable:
                SetState(UpdateInstallState.Idle);
                await _services.Desktop.OpenUrlAsync(release.PageUrl).ConfigureAwait(true);
                break;
            default:
                SetState(UpdateInstallState.Failed, result switch
                {
                    UpdateInstallResult.AssetMissing => UpdateFailure.MissingInstaller,
                    UpdateInstallResult.VerificationFailed => UpdateFailure.Verification,
                    UpdateInstallResult.LaunchFailed => UpdateFailure.Start,
                    UpdateInstallResult.InstallerFailed => UpdateFailure.Installer,
                    _ => UpdateFailure.Download,
                });
                break;
        }
    }

    /// <summary>Cancels the download (not available once the installer has started).</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelUpdate() => _installCancel?.Cancel();

    /// <summary>"What's new": the release page.</summary>
    [RelayCommand]
    private async Task WhatsNewAsync()
    {
        if (_release is not null)
        {
            await _services.Desktop.OpenUrlAsync(_release.PageUrl).ConfigureAwait(true);
        }
    }

    /// <summary>"Download" (portable): the latest-release page, where the user downloads the new exe again.</summary>
    [RelayCommand]
    private Task DownloadAsync() => _services.Desktop.OpenUrlAsync(AppLinks.LatestRelease);

    [RelayCommand]
    private void Later()
    {
        IsBannerVisible = false;
        SetState(UpdateInstallState.Idle);
    }

    [RelayCommand]
    private void Skip()
    {
        if (_release is not null)
        {
            _check?.SkipVersion(_release.Version);
        }

        IsBannerVisible = false;
    }

    [RelayCommand]
    private void DismissNotice() => IsNoticeVisible = false;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNowAsync()
    {
        if (_check is null)
        {
            return;
        }

        SetOutcome(UpdateCheckOutcome.Checking);
        try
        {
            var result = await _check.CheckAsync(UpdateCheckMode.Manual, _lifetime.Token).ConfigureAwait(true);
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Release is not null:
                    ShowRelease(result.Release);
                    SetOutcome(UpdateCheckOutcome.Available);
                    break;
                case UpdateCheckStatus.UpToDate:
                    SetOutcome(UpdateCheckOutcome.UpToDate);
                    break;
                default:
                    SetOutcome(UpdateCheckOutcome.Failed);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            SetOutcome(UpdateCheckOutcome.None);
        }
#pragma warning disable CA1031 // The service should not throw, but a bug must show "couldn't check", not crash.
        catch (Exception)
#pragma warning restore CA1031
        {
            SetOutcome(UpdateCheckOutcome.Failed);
        }
    }

    private bool CanCheckNow() => !IsChecking;

    // ---------- Wiring ----------

    /// <summary>Window closing: cancels a running check and ends the wait for a running installer.</summary>
    public void Shutdown()
    {
        if (!_disposed)
        {
            CancelInstallShutdown();
            _lifetime.Cancel();
        }
    }

    private void CancelInstallShutdown()
    {
        try
        {
            _installShutdown?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run just finished and released its token.
        }
    }

    /// <summary>
    /// Windows asked the app to shut down (Restart Manager, sign-out). Ends the wait for a running installer only;
    /// if the shutdown is cancelled and the app lives on, update checks keep working.
    /// </summary>
    public void NotifyShutdownRequested()
    {
        if (!_disposed)
        {
            CancelInstallShutdown();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Dispose();
    }

    public void UpdateStrings(LocalizedStrings strings)
    {
        _l = strings;
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerSubtitle));
        OnPropertyChanged(nameof(BannerAnnouncement));
        OnPropertyChanged(nameof(CheckResultText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(FailureText));
        OnPropertyChanged(nameof(ProgressText));
    }

    private async Task RunAutomaticCheckAsync()
    {
        try
        {
            var result = await _check!.CheckAsync(UpdateCheckMode.Automatic, _lifetime.Token).ConfigureAwait(true);
            // The user may have switched the check off while the request was running.
            if (AutoCheck && result.Status == UpdateCheckStatus.UpdateAvailable && result.Release is not null)
            {
                ShowRelease(result.Release);
            }
        }
#pragma warning disable CA1031 // The automatic check must never bother the user, whatever goes wrong.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void ShowRelease(ReleaseInfo release)
    {
        _release = release;
        IsBannerVisible = true;
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerSubtitle));
        OnPropertyChanged(nameof(BannerAnnouncement));
    }

    private void SetOutcome(UpdateCheckOutcome outcome)
    {
        _outcome = outcome;
        OnPropertyChanged(nameof(Outcome));
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(HasCheckResult));
        OnPropertyChanged(nameof(IsCheckFailed));
        OnPropertyChanged(nameof(CheckResultText));
        CheckNowCommand.NotifyCanExecuteChanged();
    }
}
