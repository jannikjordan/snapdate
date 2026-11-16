namespace Snapdate.Application.Updates;

public enum UpdateCheckStatus
{
    /// <summary>Automatic check not performed (switched off, or already checked within the last day).</summary>
    NotDue,

    /// <summary>Nothing could be learned (offline, error, rate limit, bad answer); show nothing.</summary>
    NoInformation,

    /// <summary>The running version is the newest (or newer than the latest stable release).</summary>
    UpToDate,

    /// <summary>A newer release exists but the user chose to skip it (automatic checks only).</summary>
    VersionSkipped,

    /// <summary>A newer stable release exists; see <see cref="UpdateCheckResult.Release"/>.</summary>
    UpdateAvailable,
}
