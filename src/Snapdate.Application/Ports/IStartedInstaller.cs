namespace Snapdate.Application.Ports;

/// <summary>A running installer process started by <see cref="IInstallerLauncher"/>.</summary>
public interface IStartedInstaller : IDisposable
{
    /// <summary>
    /// Completes with the installer's exit code (0 = success) when the process has ended.
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled first;
    /// the process itself keeps running in that case.
    /// </summary>
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}
