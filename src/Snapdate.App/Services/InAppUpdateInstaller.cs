using Snapdate.Application.Updates;

namespace Snapdate.App.Services;

/// <summary>The in-app update: a thin adapter over <see cref="InstallUpdateUseCase"/>.</summary>
public sealed class InAppUpdateInstaller(InstallUpdateUseCase useCase) : IUpdateInstaller
{
    public bool CanInstallInApp(ReleaseInfo release) => useCase.IsApplicable(release);

    public Task<UpdateInstallResult> InstallAsync(
        ReleaseInfo release,
        IProgress<UpdateProgress> progress,
        IProgress<UpdatePhase> phase,
        CancellationToken cancellationToken,
        CancellationToken appShutdown) =>
        useCase.ExecuteAsync(release, progress, phase, cancellationToken, appShutdown);
}
