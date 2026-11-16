using Snapdate.App.Tests.Support;
using Snapdate.App.ViewModels;
using Snapdate.Application.Installation;
using Snapdate.Application.Settings;
using Snapdate.Application.Updates;

namespace Snapdate.App.Tests;

/// <summary>"Update now": every outcome of the in-app update as the user sees it. Fakes only, no network.</summary>
public sealed class InAppUpdateViewModelTests
{
    private static async Task<(TestServices Services, MainWindowViewModel Vm)> StartAsync(InstallKind kind = InstallKind.Installed)
    {
        var services = new TestServices
        {
            Feed = new FakeFeed { Latest = FakeFeed.Release("1.1.0") },
            InstallKind = kind,
            Settings = new MemorySettings(new AppSettings { UpdateNoticeShown = true }),
        };
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;
        return (services, vm);
    }

    [Fact]
    public async Task Update_now_runs_the_installer_with_the_release_and_shows_progress_then_exits_on_completion()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Phases.AddRange([UpdatePhase.Downloading]);
        services.Installer.Progress.Add(new UpdateProgress(25, 100));
        services.Installer.Progress.Add(new UpdateProgress(50, 100));
        services.Installer.Gate = new TaskCompletionSource();

        var run = vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateInstallState.Downloading, vm.Update.InstallState);
        Assert.True(vm.Update.IsBusy);
        Assert.True(vm.Update.CanCancel);
        Assert.False(vm.Update.IsOffering);
        Assert.Equal(0.5, vm.Update.Progress, 3);
        Assert.Equal("50 %", vm.Update.ProgressText);
        Assert.Equal("Downloading the update…", vm.Update.StatusText);
        Assert.Equal("1.1.0", Assert.Single(services.Installer.Started).Version.ToString());
        Assert.Equal(0, services.AppExit.Exits);

        services.Installer.Gate.SetResult();
        await run;

        Assert.Equal(1, services.AppExit.Exits); // Completed: the new version is already running
    }

    [Fact]
    public async Task Verifying_and_installing_steps_are_shown_and_the_wait_has_no_cancel_button()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Phases.AddRange([UpdatePhase.Downloading, UpdatePhase.Verifying, UpdatePhase.Installing]);
        services.Installer.Gate = new TaskCompletionSource();

        var run = vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateInstallState.Installing, vm.Update.InstallState);
        Assert.False(vm.Update.CanCancel);
        Assert.Contains("Windows may ask for permission", vm.Update.StatusText, StringComparison.Ordinal);
        Assert.Equal(vm.Update.StatusText, vm.Update.BannerAnnouncement);
        Assert.Equal(0, services.AppExit.Exits); // the app never exits by itself while waiting

        vm.Update.Shutdown(); // the window is being closed: the wait ends
        services.Installer.Result = UpdateInstallResult.InstallerRunning;
        services.Installer.Gate.SetResult();
        await run;
    }

    [Fact]
    public async Task Installer_running_result_while_the_app_stays_alive_resets_the_banner_and_does_not_exit()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Phases.AddRange([UpdatePhase.Downloading, UpdatePhase.Installing]);
        services.Installer.Result = UpdateInstallResult.InstallerRunning;

        await vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateInstallState.Idle, vm.Update.InstallState); // the app is still alive: back to normal
        Assert.False(vm.Update.IsBannerVisible);
        Assert.Equal(0, services.AppExit.Exits);
    }

    [Fact]
    public async Task Window_shutdown_signals_the_app_shutdown_token_given_to_the_installer()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Gate = new TaskCompletionSource();
        var run = vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.False(services.Installer.SeenShutdown.IsCancellationRequested);
        vm.Shutdown();

        Assert.True(services.Installer.SeenShutdown.IsCancellationRequested);
        await run;
    }

    [Fact]
    public async Task Cancel_returns_to_the_normal_banner()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Phases.Add(UpdatePhase.Downloading);
        services.Installer.Gate = new TaskCompletionSource();
        var run = vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.True(vm.Update.CancelUpdateCommand.CanExecute(null));
        vm.Update.CancelUpdateCommand.Execute(null);
        await run;

        Assert.Equal(UpdateInstallState.Idle, vm.Update.InstallState);
        Assert.True(vm.Update.IsOffering);
        Assert.True(vm.Update.IsBannerVisible);
        Assert.Equal(0, services.AppExit.Exits);
        Assert.Empty(services.Desktop.OpenedUrls);
    }

    [Theory]
    [InlineData(UpdateInstallResult.DownloadFailed, UpdateFailure.Download)]
    [InlineData(UpdateInstallResult.VerificationFailed, UpdateFailure.Verification)]
    [InlineData(UpdateInstallResult.AssetMissing, UpdateFailure.MissingInstaller)]
    [InlineData(UpdateInstallResult.LaunchFailed, UpdateFailure.Start)]
    [InlineData(UpdateInstallResult.InstallerFailed, UpdateFailure.Installer)]
    public async Task Failures_show_a_message_and_offer_the_release_page(UpdateInstallResult result, UpdateFailure expected)
    {
        var (services, vm) = await StartAsync();
        services.Installer.Result = result;

        await vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateInstallState.Failed, vm.Update.InstallState);
        Assert.True(vm.Update.IsFailed);
        Assert.Equal(expected, vm.Update.Failure);
        Assert.NotEmpty(vm.Update.FailureText);
        Assert.StartsWith("The update did not work", vm.Update.BannerAnnouncement, StringComparison.Ordinal);
        Assert.Equal(0, services.AppExit.Exits);

        await vm.Update.WhatsNewCommand.ExecuteAsync(null); // the failed banner's "Open release page"
        Assert.Equal([FakeFeed.Release("1.1.0").PageUrl], services.Desktop.OpenedUrls);
    }

    [Fact]
    public async Task Failure_text_follows_the_language_and_later_dismisses_it()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Result = UpdateInstallResult.InstallerFailed;
        await vm.Update.UpdateNowCommand.ExecuteAsync(null);
        var english = vm.Update.FailureText;

        vm.IsGerman = true;
        Assert.NotEqual(english, vm.Update.FailureText);
        Assert.Contains("Installation wurde abgebrochen", vm.Update.FailureText, StringComparison.Ordinal);

        vm.Update.LaterCommand.Execute(null);
        Assert.False(vm.Update.IsBannerVisible);
        Assert.Equal(UpdateInstallState.Idle, vm.Update.InstallState);
    }

    [Fact]
    public async Task An_installer_that_throws_is_reported_as_a_failed_download_not_a_crash()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Throw = new InvalidOperationException("bug");

        await vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateFailure.Download, vm.Update.Failure);
    }

    [Fact]
    public async Task When_the_in_app_path_does_not_apply_the_release_page_opens_and_nothing_is_installed()
    {
        var (services, vm) = await StartAsync();
        services.Installer.CanInstall = false;

        await vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Empty(services.Installer.Started);
        Assert.Equal([FakeFeed.Release("1.1.0").PageUrl], services.Desktop.OpenedUrls);
        Assert.Equal(UpdateInstallState.Idle, vm.Update.InstallState);
    }

    [Fact]
    public async Task Not_applicable_result_falls_back_to_the_release_page()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Result = UpdateInstallResult.NotApplicable;

        await vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal([FakeFeed.Release("1.1.0").PageUrl], services.Desktop.OpenedUrls);
        Assert.Equal(UpdateInstallState.Idle, vm.Update.InstallState);
    }

    [Fact]
    public async Task Shutdown_request_that_is_cancelled_resets_the_banner_and_keeps_update_checks_working()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Phases.AddRange([UpdatePhase.Downloading, UpdatePhase.Installing]);
        services.Installer.Gate = new TaskCompletionSource();
        var run = vm.Update.UpdateNowCommand.ExecuteAsync(null);
        Assert.Equal(UpdateInstallState.Installing, vm.Update.InstallState);

        vm.Update.NotifyShutdownRequested(); // Windows asked to shut down ...
        await run; // ... but the sign-out was cancelled: the app is still alive

        Assert.True(services.Installer.SeenShutdown.IsCancellationRequested);
        Assert.Equal(UpdateInstallState.Idle, vm.Update.InstallState);
        Assert.False(vm.Update.IsBannerVisible);
        Assert.Equal(0, services.AppExit.Exits);

        await vm.Update.CheckNowCommand.ExecuteAsync(null); // the permanent lifetime token was not touched
        Assert.Equal(UpdateCheckOutcome.Available, vm.Update.Outcome);
        Assert.True(vm.Update.IsBannerVisible);
    }

    [Theory]
    [InlineData(UpdateInstallResult.DownloadFailed, true)]
    [InlineData(UpdateInstallResult.LaunchFailed, true)]
    [InlineData(UpdateInstallResult.InstallerFailed, true)]
    [InlineData(UpdateInstallResult.VerificationFailed, false)]
    [InlineData(UpdateInstallResult.AssetMissing, false)]
    public async Task Try_again_is_offered_only_where_it_can_help(UpdateInstallResult result, bool canRetry)
    {
        var (services, vm) = await StartAsync();
        services.Installer.Result = result;

        await vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(canRetry, vm.Update.CanRetry);
    }

    [Fact]
    public async Task Try_again_runs_the_update_again()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Result = UpdateInstallResult.DownloadFailed;
        await vm.Update.UpdateNowCommand.ExecuteAsync(null);
        Assert.True(vm.Update.IsFailed);

        services.Installer.Result = UpdateInstallResult.Completed;
        await vm.Update.UpdateNowCommand.ExecuteAsync(null); // the banner's "Try again" is this command

        Assert.Equal(2, services.Installer.Started.Count);
        Assert.Equal(1, services.AppExit.Exits);
    }

    [Fact]
    public async Task A_second_click_while_busy_does_nothing()
    {
        var (services, vm) = await StartAsync();
        services.Installer.Gate = new TaskCompletionSource();
        var first = vm.Update.UpdateNowCommand.ExecuteAsync(null);

        await vm.Update.UpdateNowCommand.ExecuteAsync(null);

        Assert.Single(services.Installer.Started);
        services.Installer.Gate.SetResult();
        await first;
    }
}
