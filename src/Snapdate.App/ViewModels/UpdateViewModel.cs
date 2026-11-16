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
    private readonly IUpdateInstaller _installer;
    private readonly CancellationTokenSource _lifetime = new();
    private ReleaseInfo? _release;
    private UpdateCheckOutcome _outcome;
    private LocalizedStrings _l;

    public UpdateViewModel(AppServices services, LocalizedStrings strings)
    {
        _services = services;
        _l = strings;
        _check = services.UpdateCheck;
        _install = services.Install?.Current ?? InstallInfo.Portable;
        _installer = services.UpdateInstaller ?? new OpenReleasePageUpdateInstaller(services.Desktop);

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

    public string BannerAnnouncement => $"{BannerTitle}. {BannerSubtitle}";

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

    /// <summary>"Update now": hands over to the <see cref="IUpdateInstaller"/> (installed builds only).</summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_release is not null)
        {
            await _installer.UpdateNowAsync(_release, _install).ConfigureAwait(true);
        }
    }

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
    private void Later() => IsBannerVisible = false;

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

    /// <summary>Cancels a running check (window closing).</summary>
    public void Shutdown() => _lifetime.Cancel();

    public void Dispose() => _lifetime.Dispose();

    public void UpdateStrings(LocalizedStrings strings)
    {
        _l = strings;
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerSubtitle));
        OnPropertyChanged(nameof(BannerAnnouncement));
        OnPropertyChanged(nameof(CheckResultText));
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
