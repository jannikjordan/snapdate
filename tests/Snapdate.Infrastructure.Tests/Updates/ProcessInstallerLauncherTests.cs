using Snapdate.Application.Installation;
using Snapdate.Infrastructure.Updates;

namespace Snapdate.Infrastructure.Tests.Updates;

public sealed class ProcessInstallerLauncherTests
{
    [Fact]
    public void Current_user_arguments()
    {
        Assert.Equal(
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /RESTARTAPP",
            ProcessInstallerLauncher.BuildArguments(InstallScope.CurrentUser));
    }

    [Fact]
    public void All_users_arguments()
    {
        Assert.Equal(
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLUSERS /RESTARTAPP",
            ProcessInstallerLauncher.BuildArguments(InstallScope.AllUsers));
    }

    [Fact]
    public void Portable_scope_has_no_arguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessInstallerLauncher.BuildArguments(InstallScope.None));
    }

    [Fact]
    public void A_missing_installer_reports_failure_instead_of_throwing()
    {
        Assert.Null(new ProcessInstallerLauncher().Launch(Path.Combine(Path.GetTempPath(), "snapdate-does-not-exist", "Setup.exe"), InstallScope.CurrentUser));
    }
}
