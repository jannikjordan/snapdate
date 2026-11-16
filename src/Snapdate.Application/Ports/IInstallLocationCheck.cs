using Snapdate.Application.Installation;

namespace Snapdate.Application.Ports;

/// <summary>Cross-checks the install marker file against the installer's registry entry.</summary>
public interface IInstallLocationCheck
{
    /// <summary>
    /// True only when the installer's uninstall entry for <paramref name="scope"/> names the folder of the running exe
    /// as its install location. A forged marker without a matching registry entry yields false. Never throws.
    /// </summary>
    bool IsRegisteredForRunningExe(InstallScope scope);
}
