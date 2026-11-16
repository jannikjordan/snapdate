using Snapdate.Application.Installation;
using Snapdate.Application.Updates;

namespace Snapdate.App.Services;

/// <summary>
/// What "Update now" does. This is the seam for the in-app update (#3): the installer-based
/// implementation replaces <see cref="OpenReleasePageUpdateInstaller"/> for <see cref="InstallKind.Installed"/>.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>Starts the update to <paramref name="release"/> for the given kind of installation.</summary>
    Task UpdateNowAsync(ReleaseInfo release, InstallInfo install);
}
