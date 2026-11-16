using Snapdate.Application.Updates;

namespace Snapdate.Application.Ports;

/// <summary>Fetches information about the latest published Snapdate release.</summary>
public interface IReleaseFeed
{
    /// <summary>
    /// Returns the latest release, or null when nothing trustworthy could be learned (offline, timeout,
    /// HTTP error, rate limit, malformed or oversized answer). Implementations must not throw for those
    /// cases; only cancellation requested through <paramref name="cancellationToken"/> may surface.
    /// </summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken);
}
