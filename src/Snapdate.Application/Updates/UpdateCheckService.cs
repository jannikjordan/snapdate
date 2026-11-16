using Snapdate.Application.Ports;
using Snapdate.Domain.Versioning;

namespace Snapdate.Application.Updates;

/// <summary>
/// Decides whether and when to ask the release feed for a newer version, and interprets the answer.
/// Never throws for feed problems; the UI only ever sees a <see cref="UpdateCheckResult"/>.
/// </summary>
public sealed class UpdateCheckService(
    IReleaseFeed feed,
    ISettingsStore settingsStore,
    IClock clock,
    AppVersion currentVersion)
{
    /// <summary>Minimum time between automatic checks.</summary>
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Runs a check. The time of the last check is stored only when the feed gave an answer, so an
    /// offline start does not suppress the next attempt. Caller cancellation is propagated.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckMode mode, CancellationToken cancellationToken = default)
    {
        var settings = settingsStore.Load();
        var now = clock.UtcNow;

        if (mode == UpdateCheckMode.Automatic
            && (!settings.AutoUpdateCheck || !IsDue(settings.LastUpdateCheckUtc, now)))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NotDue);
        }

        ReleaseInfo? release;
        try
        {
            release = await feed.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The feed contract says "no throw", but a bug there must never reach the user.
            return new UpdateCheckResult(UpdateCheckStatus.NoInformation);
        }

        if (release is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoInformation);
        }

        // Re-load: the user may have changed settings while the request was running.
        settingsStore.Save(settingsStore.Load() with { LastUpdateCheckUtc = now });

        if (release.IsDraft || release.IsPreRelease || release.Version.IsPreRelease)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }

        if (release.Version <= currentVersion)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }

        if (mode == UpdateCheckMode.Automatic && IsSkipped(settings.SkippedVersion, release.Version))
        {
            return new UpdateCheckResult(UpdateCheckStatus.VersionSkipped, release);
        }

        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release);
    }

    /// <summary>Remembers that the user does not want to be told about <paramref name="version"/> automatically.</summary>
    public void SkipVersion(AppVersion version) =>
        settingsStore.Save(settingsStore.Load() with { SkippedVersion = version.ToString() });

    private static bool IsDue(DateTimeOffset? last, DateTimeOffset now) =>
        last is null || last > now || now - last.Value >= AutomaticInterval;

    private static bool IsSkipped(string? skipped, AppVersion candidate) =>
        AppVersion.TryParse(skipped, out var v) && v == candidate;
}
