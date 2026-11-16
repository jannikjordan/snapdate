using Snapdate.Application.Installation;
using Snapdate.Infrastructure.Installation;

namespace Snapdate.Infrastructure.Tests.Installation;

public sealed class RegistryInstallLocationCheckTests
{
    private const string Exe = @"C:\Users\someone\AppData\Local\Programs\Snapdate";

    private static RegistryInstallLocationCheck Create(string? exeFolder, params string[] registered) =>
        new(exeFolder, _ => registered);

    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\Programs\Snapdate")]
    [InlineData(@"C:\Users\someone\AppData\Local\Programs\Snapdate\")]
    [InlineData(@"c:\users\SOMEONE\appdata\local\programs\snapdate\")]
    [InlineData(@"C:\Users\someone\AppData\Local\Programs\Snapdate\..\Snapdate")]
    public void Matching_install_location_is_accepted_ignoring_case_and_trailing_separator(string registered)
    {
        Assert.True(Create(Exe, registered).IsRegisteredForRunningExe(InstallScope.CurrentUser));
        Assert.True(Create(Exe + @"\", registered).IsRegisteredForRunningExe(InstallScope.AllUsers));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\Programs\Other")]
    [InlineData(@"C:\Users\someone\AppData\Local\Programs\Snapdate\sub")]
    [InlineData(@"C:\Users\someone\AppData\Local\Programs")]
    [InlineData(@"relative\Snapdate")]
    [InlineData("")]
    [InlineData("   ")]
    public void Different_or_unusable_install_location_is_rejected(string registered)
    {
        Assert.False(Create(Exe, registered).IsRegisteredForRunningExe(InstallScope.CurrentUser));
    }

    [Fact]
    public void No_registry_entry_means_a_forged_marker()
    {
        Assert.False(Create(Exe).IsRegisteredForRunningExe(InstallScope.CurrentUser));
    }

    [Fact]
    public void Any_of_several_registry_views_may_match()
    {
        Assert.True(Create(Exe, @"D:\Elsewhere", Exe).IsRegisteredForRunningExe(InstallScope.AllUsers));
    }

    [Theory]
    [InlineData(InstallScope.None)]
    [InlineData((InstallScope)99)]
    public void Not_installed_scopes_are_rejected(InstallScope scope)
    {
        Assert.False(Create(Exe, Exe).IsRegisteredForRunningExe(scope));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    public void Unknown_exe_folder_is_rejected(string? exeFolder)
    {
        Assert.False(Create(exeFolder, Exe).IsRegisteredForRunningExe(InstallScope.CurrentUser));
    }

    [Fact]
    public void Registry_errors_mean_not_registered()
    {
        var check = new RegistryInstallLocationCheck(Exe, _ => throw new UnauthorizedAccessException());
        Assert.False(check.IsRegisteredForRunningExe(InstallScope.AllUsers));
    }

    [Fact]
    public void The_real_registry_has_no_entry_for_the_test_host()
    {
        // The test runner is not an installed Snapdate: no uninstall entry may point at its folder.
        Assert.False(new RegistryInstallLocationCheck().IsRegisteredForRunningExe(InstallScope.CurrentUser));
        Assert.False(new RegistryInstallLocationCheck().IsRegisteredForRunningExe(InstallScope.AllUsers));
    }
}
