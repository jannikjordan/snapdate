using Snapdate.Application.Installation;

namespace Snapdate.Application.Ports;

/// <summary>Tells whether the running exe was put there by the installer or is a portable copy.</summary>
public interface IInstallInfo
{
    /// <summary>
    /// Read once and then fixed for the life of the process. Never throws: when the install cannot be
    /// proven (no marker, unreadable, malformed) the result is <see cref="InstallInfo.Portable"/>.
    /// </summary>
    InstallInfo Current { get; }
}
