namespace Snapdate.Application.Updates;

/// <summary>Outcome of an update check. <see cref="Release"/> is set for UpdateAvailable and VersionSkipped.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release = null)
{
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;
}
