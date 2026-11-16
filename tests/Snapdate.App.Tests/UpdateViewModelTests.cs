using Snapdate.App.Tests.Support;
using Snapdate.App.ViewModels;
using Snapdate.Application.Installation;
using Snapdate.Application.Settings;

namespace Snapdate.App.Tests;

/// <summary>The update check as the user sees it. Fake feed and clock only: no test touches the network.</summary>
public sealed class UpdateViewModelTests
{
    private static TestServices WithNewerRelease(InstallKind kind = InstallKind.Installed, AppSettings? settings = null) =>
        new()
        {
            Feed = new FakeFeed { Latest = FakeFeed.Release("1.1.0") },
            InstallKind = kind,
            Settings = new MemorySettings(settings ?? new AppSettings { UpdateNoticeShown = true }),
        };

    [Fact]
    public async Task Start_check_shows_the_banner_when_a_newer_version_exists()
    {
        var services = WithNewerRelease();
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;

        Assert.True(vm.Update.IsBannerVisible);
        Assert.Equal("Snapdate 1.1.0 is available", vm.Update.BannerTitle);
        Assert.Equal(1, services.Feed!.Calls);
    }

    [Fact]
    public async Task Banner_follows_the_language()
    {
        var services = WithNewerRelease();
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;

        vm.IsGerman = true;

        Assert.Equal("Snapdate 1.1.0 ist verfügbar", vm.Update.BannerTitle);
    }

    [Fact]
    public async Task Start_check_is_silent_when_up_to_date_or_offline()
    {
        var upToDate = new TestServices { Feed = new FakeFeed { Latest = FakeFeed.Release("1.0.0") } };
        var offline = new TestServices { Feed = new FakeFeed { Latest = null } };

        var a = upToDate.CreateViewModel();
        var b = offline.CreateViewModel();
        await a.Update.AutomaticCheckCompletion;
        await b.Update.AutomaticCheckCompletion;

        Assert.False(a.Update.IsBannerVisible);
        Assert.False(b.Update.IsBannerVisible);
        Assert.False(b.Update.HasCheckResult);
    }

    [Fact]
    public async Task Turning_the_check_off_means_no_request_on_start()
    {
        var services = WithNewerRelease(settings: new AppSettings { AutoUpdateCheck = false });
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;

        Assert.Equal(0, services.Feed!.Calls);
        Assert.False(vm.Update.IsBannerVisible);
        Assert.False(vm.Update.AutoCheck);
    }

    [Fact]
    public void Toggle_is_saved_and_keeps_other_update_settings()
    {
        var services = WithNewerRelease(settings: new AppSettings { UpdateNoticeShown = true, SkippedVersion = "0.9.0" });
        var vm = services.CreateViewModel();

        vm.Update.AutoCheck = false;
        vm.SaveSettings();

        Assert.False(services.Settings.Current.AutoUpdateCheck);
        Assert.Equal("0.9.0", services.Settings.Current.SkippedVersion);
    }

    [Fact]
    public async Task Later_hides_the_banner_without_remembering()
    {
        var services = WithNewerRelease();
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;

        vm.Update.LaterCommand.Execute(null);

        Assert.False(vm.Update.IsBannerVisible);
        Assert.Null(services.Settings.Current.SkippedVersion);
    }

    [Fact]
    public async Task Skip_hides_the_banner_and_is_persisted()
    {
        var services = WithNewerRelease();
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;

        vm.Update.SkipCommand.Execute(null);

        Assert.False(vm.Update.IsBannerVisible);
        Assert.Equal("1.1.0", services.Settings.Current.SkippedVersion);

        // The next start (a day later) stays quiet about that version.
        services.Clock.UtcNow = services.Clock.UtcNow.AddDays(2);
        var next = services.CreateViewModel();
        await next.Update.AutomaticCheckCompletion;
        Assert.False(next.Update.IsBannerVisible);
    }

    [Fact]
    public async Task Portable_build_shows_Download_only_and_opens_the_latest_release_page()
    {
        var services = WithNewerRelease(InstallKind.Portable);
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;

        Assert.True(vm.Update.IsPortable);
        Assert.False(vm.Update.IsInstalled);

        await vm.Update.DownloadCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://github.com/jannikjordan/snapdate/releases/latest")], services.Desktop.OpenedUrls);
        Assert.Empty(services.Installer.Started);
    }

    [Fact]
    public async Task Manual_check_reports_up_to_date()
    {
        var services = new TestServices { Feed = new FakeFeed { Latest = FakeFeed.Release("1.0.0") } };
        var vm = services.CreateViewModel();

        await vm.Update.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateCheckOutcome.UpToDate, vm.Update.Outcome);
        Assert.Matches(@"^You're up to date \(version \S+\)$", vm.Update.CheckResultText);
        Assert.False(vm.Update.IsBannerVisible);
    }

    [Fact]
    public async Task Manual_check_with_a_newer_version_shows_the_banner_even_if_skipped()
    {
        var services = WithNewerRelease(settings: new AppSettings { UpdateNoticeShown = true, SkippedVersion = "1.1.0" });
        var vm = services.CreateViewModel();
        await vm.Update.AutomaticCheckCompletion;
        Assert.False(vm.Update.IsBannerVisible);

        await vm.Update.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateCheckOutcome.Available, vm.Update.Outcome);
        Assert.Equal("Snapdate 1.1.0 is available", vm.Update.CheckResultText);
        Assert.True(vm.Update.IsBannerVisible);
    }

    [Fact]
    public async Task Manual_check_says_so_when_offline()
    {
        var services = new TestServices { Feed = new FakeFeed { Latest = null } };
        var vm = services.CreateViewModel();

        await vm.Update.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdateCheckOutcome.Failed, vm.Update.Outcome);
        Assert.Equal("Couldn't check right now — are you offline?", vm.Update.CheckResultText);
    }

    [Fact]
    public async Task Manual_check_is_busy_while_running_and_cancelled_by_shutdown()
    {
        var feed = new FakeFeed { Latest = FakeFeed.Release("1.1.0"), Gate = new TaskCompletionSource() };
        var services = new TestServices { Feed = feed };
        var vm = services.CreateViewModel();

        var running = vm.Update.CheckNowCommand.ExecuteAsync(null);

        Assert.True(vm.Update.IsChecking);
        Assert.False(vm.Update.CheckNowCommand.CanExecute(null));

        vm.Shutdown();
        await running;

        Assert.False(vm.Update.IsChecking);
        Assert.False(vm.Update.HasCheckResult);
        Assert.False(vm.Update.IsBannerVisible);
    }

    [Fact]
    public async Task First_start_notice_is_shown_once()
    {
        var services = new TestServices { Feed = new FakeFeed { Latest = FakeFeed.Release("1.0.0") } };

        var first = services.CreateViewModel();
        await first.Update.AutomaticCheckCompletion;
        Assert.True(first.Update.IsNoticeVisible);
        Assert.True(services.Settings.Current.UpdateNoticeShown);

        var second = services.CreateViewModel();
        Assert.False(second.Update.IsNoticeVisible);

        first.Update.DismissNoticeCommand.Execute(null);
        Assert.False(first.Update.IsNoticeVisible);
    }

    [Fact]
    public async Task First_start_shows_the_notice_and_makes_no_request_the_check_follows_on_the_next_start()
    {
        var services = new TestServices { Feed = new FakeFeed { Latest = FakeFeed.Release("1.1.0") } };

        var first = services.CreateViewModel();
        await first.Update.AutomaticCheckCompletion;
        Assert.True(first.Update.IsNoticeVisible);
        Assert.Equal(0, services.Feed!.Calls);
        Assert.False(first.Update.IsBannerVisible);
        Assert.Null(services.Settings.Current.LastUpdateCheckUtc);

        var second = services.CreateViewModel();
        await second.Update.AutomaticCheckCompletion;
        Assert.Equal(1, services.Feed.Calls);
        Assert.True(second.Update.IsBannerVisible);
    }

    [Fact]
    public void Creating_the_view_model_does_not_write_settings_for_the_toggle()
    {
        var services = new TestServices
        {
            Feed = new FakeFeed { Gate = new TaskCompletionSource() },
            Settings = new MemorySettings(new AppSettings { UpdateNoticeShown = true }),
        };

        var vm = services.CreateViewModel();
        vm.Shutdown();

        Assert.Equal(0, services.Settings.Saves);
    }

    [Fact]
    public async Task A_result_that_arrives_after_the_check_was_switched_off_is_ignored()
    {
        var feed = new FakeFeed { Latest = FakeFeed.Release("1.1.0"), Gate = new TaskCompletionSource() };
        var services = new TestServices
        {
            Feed = feed,
            Settings = new MemorySettings(new AppSettings { UpdateNoticeShown = true }),
        };
        var vm = services.CreateViewModel();

        vm.Update.AutoCheck = false;
        feed.Gate.SetResult();
        await vm.Update.AutomaticCheckCompletion;

        Assert.False(vm.Update.IsBannerVisible);
    }

    [Fact]
    public void First_start_notice_is_not_shown_when_the_check_is_off()
    {
        var services = new TestServices
        {
            Feed = new FakeFeed(),
            Settings = new MemorySettings(new AppSettings { AutoUpdateCheck = false }),
        };

        var vm = services.CreateViewModel();

        Assert.False(vm.Update.IsNoticeVisible);
        Assert.False(services.Settings.Current.UpdateNoticeShown);
    }

    [Fact]
    public void Turning_the_check_off_hides_the_notice()
    {
        var services = new TestServices { Feed = new FakeFeed() };
        var vm = services.CreateViewModel();
        Assert.True(vm.Update.IsNoticeVisible);

        vm.Update.AutoCheck = false;

        Assert.False(vm.Update.IsNoticeVisible);
    }

    [Fact]
    public void Without_a_known_version_the_feature_is_unavailable_and_silent()
    {
        var vm = new TestServices().CreateViewModel();

        Assert.False(vm.Update.IsAvailable);
        Assert.False(vm.Update.IsNoticeVisible);
        Assert.False(vm.Update.IsBannerVisible);
    }
}
