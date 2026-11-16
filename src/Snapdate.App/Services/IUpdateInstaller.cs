using Snapdate.Application.Updates;

namespace Snapdate.App.Services;

/// <summary>
/// What "Update now" does for an installed build: the verified in-app update (ADR 0004). The view model falls back
/// to opening the release page whenever <see cref="CanInstallInApp"/> says no.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>True when the silent in-app update applies to <paramref name="release"/> (installed, confirmed, newer).</summary>
    bool CanInstallInApp(ReleaseInfo release);

    /// <summary>Runs the update; see <see cref="InstallUpdateUseCase"/> for the contract (never throws).</summary>
    Task<UpdateInstallResult> InstallAsync(
        ReleaseInfo release,
        IProgress<UpdateProgress> progress,
        IProgress<UpdatePhase> phase,
        CancellationToken cancellationToken,
        CancellationToken appShutdown);
}
