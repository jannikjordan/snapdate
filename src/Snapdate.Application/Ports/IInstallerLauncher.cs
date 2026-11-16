using Snapdate.Application.Installation;

namespace Snapdate.Application.Ports;

/// <summary>Starts a verified installer silently in the install scope of the running copy.</summary>
public interface IInstallerLauncher
{
    /// <summary>
    /// Starts the installer without elevating this process (Setup asks for administrator rights itself when needed,
    /// by relaunching itself elevated). Returns the started process, or null when it could not be started; never throws.
    /// </summary>
    /// <param name="installerPath">Full path of the verified installer.</param>
    /// <param name="scope">Must be <see cref="InstallScope.CurrentUser"/> or <see cref="InstallScope.AllUsers"/>.</param>
    IStartedInstaller? Launch(string installerPath, InstallScope scope);
}
