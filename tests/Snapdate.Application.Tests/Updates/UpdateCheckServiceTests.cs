using Snapdate.Application.Ports;
using Snapdate.Application.Settings;
using Snapdate.Application.Updates;
using Snapdate.Domain.Versioning;

namespace Snapdate.Application.Tests.Updates;

public sealed class UpdateCheckServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class FakeStore(AppSettings? initial = null) : ISettingsStore
    {
        public AppSettings Current { get; set; } = initial ?? new AppSettings();

        public AppSettings Load() => Current;

        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class FakeFeed : IReleaseFeed
    {
        public ReleaseInfo? Release { get; set; }

        public Exception? Throw { get; set; }

        public int Calls { get; private set; }

        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Throw is null ? Task.FromResult(Release) : Task.FromException<ReleaseInfo?>(Throw);
        }
    }

    private static ReleaseInfo Release(string version, bool draft = false, bool pre = false) =>
        new(AppVersion.Parse(version), new Uri("https://github.com/jannikjordan/snapdate/releases/tag/v" + version), draft, pre, []);

    private static (UpdateCheckService Service, FakeFeed Feed, FakeStore Store, FakeClock Clock) Create(
        string current = "0.1.0", AppSettings? settings = null, ReleaseInfo? release = null)
    {
        var feed = new FakeFeed { Release = release };
        var store = new FakeStore(settings);
        var clock = new FakeClock(Now);
        return (new UpdateCheckService(feed, store, clock, AppVersion.Parse(current)), feed, store, clock);
    }

    [Fact]
    public async Task Newer_stable_release_is_offered_and_check_time_stored()
    {
        var (service, _, store, _) = Create(release: Release("0.2.0"));
        var result = await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(AppVersion.Parse("0.2.0"), result.Release!.Version);
        Assert.Equal(Now, store.Current.LastUpdateCheckUtc);
    }

    [Fact]
    public async Task Automatic_check_within_a_day_does_not_call_the_feed()
    {
        var (service, feed, _, clock) = Create(
            settings: new AppSettings { LastUpdateCheckUtc = Now - TimeSpan.FromHours(23) }, release: Release("0.2.0"));
        Assert.Equal(UpdateCheckStatus.NotDue, (await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(0, feed.Calls);

        clock.UtcNow = Now + TimeSpan.FromHours(2);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, feed.Calls);
    }

    [Fact]
    public async Task Last_check_in_the_future_counts_as_due()
    {
        var (service, feed, _, _) = Create(
            settings: new AppSettings { LastUpdateCheckUtc = Now + TimeSpan.FromDays(5) }, release: Release("0.2.0"));
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, feed.Calls);
    }

    [Fact]
    public async Task Manual_check_bypasses_the_daily_limit_and_the_opt_out()
    {
        var (service, feed, _, _) = Create(
            settings: new AppSettings { AutoUpdateCheck = false, LastUpdateCheckUtc = Now }, release: Release("0.2.0"));
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await service.CheckAsync(UpdateCheckMode.Manual, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, feed.Calls);
    }

    [Fact]
    public async Task Disabled_auto_check_never_calls_the_feed()
    {
        var (service, feed, _, _) = Create(settings: new AppSettings { AutoUpdateCheck = false }, release: Release("0.2.0"));
        Assert.Equal(UpdateCheckStatus.NotDue, (await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(0, feed.Calls);
    }

    [Fact]
    public async Task Skipped_version_is_hidden_for_automatic_but_shown_for_manual_checks()
    {
        var (service, _, _, _) = Create(settings: new AppSettings { SkippedVersion = "0.2.0" }, release: Release("0.2.0"));
        var auto = await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateCheckStatus.VersionSkipped, auto.Status);
        Assert.False(auto.IsUpdateAvailable);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await service.CheckAsync(UpdateCheckMode.Manual, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Skipping_an_older_version_does_not_hide_a_newer_release()
    {
        var (service, _, _, _) = Create(settings: new AppSettings { SkippedVersion = "0.2.0" }, release: Release("0.3.0"));
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public void SkipVersion_persists_and_keeps_other_settings()
    {
        var (service, _, store, _) = Create(settings: new AppSettings { Theme = "dark" });
        service.SkipVersion(AppVersion.Parse("v0.2.0"));
        Assert.Equal("0.2.0", store.Current.SkippedVersion);
        Assert.Equal("dark", store.Current.Theme);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.2.0", "0.1.0")]
    [InlineData("0.2.0", "0.2.0+build5")]
    [InlineData("1.0.0", "0.9.9")]
    public async Task Same_or_older_release_is_up_to_date(string current, string latest)
    {
        var (service, _, _, _) = Create(current, release: Release(latest));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await service.CheckAsync(UpdateCheckMode.Manual, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Running_a_prerelease_of_the_same_core_version_is_offered_the_stable_release()
    {
        var (service, _, _, _) = Create("0.2.0-rc.1", release: Release("0.2.0"));
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await service.CheckAsync(UpdateCheckMode.Manual, TestContext.Current.CancellationToken)).Status);
    }

    [Theory]
    [InlineData("0.2.0", true, false)]
    [InlineData("0.2.0", false, true)]
    [InlineData("0.2.0-rc.1", false, false)]
    public async Task Drafts_and_prereleases_are_ignored(string version, bool draft, bool pre)
    {
        var (service, _, _, _) = Create(release: Release(version, draft, pre));
        var result = await service.CheckAsync(UpdateCheckMode.Manual, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task No_answer_from_the_feed_is_silent_and_does_not_start_the_daily_limit()
    {
        var (service, feed, store, _) = Create(release: null);
        Assert.Equal(UpdateCheckStatus.NoInformation, (await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken)).Status);
        Assert.Null(store.Current.LastUpdateCheckUtc);

        feed.Release = Release("0.2.0");
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, (await service.CheckAsync(UpdateCheckMode.Automatic, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task A_misbehaving_feed_never_throws_to_the_caller()
    {
        var (service, feed, _, _) = Create();
        feed.Throw = new HttpRequestException("boom");
        Assert.Equal(UpdateCheckStatus.NoInformation, (await service.CheckAsync(UpdateCheckMode.Manual, TestContext.Current.CancellationToken)).Status);
        feed.Throw = new TaskCanceledException("timeout");
        Assert.Equal(UpdateCheckStatus.NoInformation, (await service.CheckAsync(UpdateCheckMode.Manual, TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task Cancellation_requested_by_the_caller_is_propagated()
    {
        var (service, _, _, _) = Create(release: Release("0.2.0"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync(UpdateCheckMode.Manual, cts.Token));
    }
}
