using Snapdate.Application.Installation;
using Snapdate.Application.Updates;

namespace Snapdate.App.Services;

/// <summary>Interim "Update now": opens the release page in the browser (no download or install by Snapdate).</summary>
public sealed class OpenReleasePageUpdateInstaller(IDesktopServices desktop) : IUpdateInstaller
{
    public Task UpdateNowAsync(ReleaseInfo release, InstallInfo install) => desktop.OpenUrlAsync(release.PageUrl);
}
