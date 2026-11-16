using System.Text;
using Snapdate.Application.Installation;
using Snapdate.Infrastructure.Installation;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.Infrastructure.Tests.Installation;

public sealed class FileInstallInfoTests
{
    private const string Marker = "snapdate-install.ini";

    [Theory]
    [InlineData("[Install]\r\nScope=user\r\n", InstallScope.CurrentUser)]
    [InlineData("[Install]\r\nScope=all\r\n", InstallScope.AllUsers)]
    [InlineData("[Install]\nScope=all", InstallScope.AllUsers)]
    [InlineData("; written by the installer\r\n\r\n[Install]\r\n Scope = user \r\n", InstallScope.CurrentUser)]
    [InlineData("[Install]\r\nFutureKey=1\r\nScope=user\r\n", InstallScope.CurrentUser)]
    public void Valid_marker_next_to_the_exe_means_installed(string content, InstallScope expected)
    {
        using var folder = new TempFolder();
        folder.Write(Marker, Encoding.UTF8.GetBytes(content));

        var info = new FileInstallInfo(folder.Combine("Snapdate.exe")).Current;

        Assert.Equal(new InstallInfo(InstallKind.Installed, expected), info);
    }

    [Fact]
    public void Marker_with_a_byte_order_mark_is_accepted()
    {
        using var folder = new TempFolder();
        folder.Write(Marker, [.. Encoding.UTF8.GetPreamble(), .. "[Install]\r\nScope=user\r\n"u8.ToArray()]);

        Assert.Equal(InstallKind.Installed, new FileInstallInfo(folder.Combine("Snapdate.exe")).Current.Kind);
    }

    [Fact]
    public void Missing_marker_means_portable()
    {
        using var folder = new TempFolder();

        var info = new FileInstallInfo(folder.Combine("Snapdate.exe")).Current;

        Assert.Equal(InstallKind.Portable, info.Kind);
        Assert.Equal(InstallScope.None, info.Scope);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Scope=user")]
    [InlineData("[Install]")]
    [InlineData("[Install]\r\nScope=")]
    [InlineData("[Install]\r\nScope=User")]
    [InlineData("[Install]\r\nScope=both")]
    [InlineData("[Install]\r\nScope=user\r\nScope=all")]
    [InlineData("[Install]\r\nScope=user\r\n[Other]\r\nX=1")]
    [InlineData("[Install]\r\nScope=user\r\n[Install]\r\nScope=user")]
    [InlineData("[install]\r\nScope=user")]
    [InlineData("[Other]\r\nScope=user")]
    [InlineData("[Install]\r\nScope=user\r\ngarbage")]
    [InlineData("[Install]\r\n=user")]
    [InlineData("\0\0\0\0")]
    public void Malformed_marker_means_portable(string content)
    {
        using var folder = new TempFolder();
        folder.Write(Marker, Encoding.UTF8.GetBytes(content));

        Assert.Equal(InstallInfo.Portable, new FileInstallInfo(folder.Combine("Snapdate.exe")).Current);
    }

    [Fact]
    public void Marker_with_invalid_utf8_means_portable()
    {
        using var folder = new TempFolder();
        folder.Write(Marker, [.. "[Install]\r\nScope=user\r\n"u8.ToArray(), 0xFF, 0xFE]);

        Assert.Equal(InstallInfo.Portable, new FileInstallInfo(folder.Combine("Snapdate.exe")).Current);
    }

    [Fact]
    public void Oversized_marker_means_portable()
    {
        using var folder = new TempFolder();
        folder.Write(Marker, Encoding.UTF8.GetBytes("[Install]\r\nScope=user\r\n" + new string(';', 2000)));

        Assert.Equal(InstallInfo.Portable, new FileInstallInfo(folder.Combine("Snapdate.exe")).Current);
    }

    [Fact]
    public void Marker_that_is_a_directory_means_portable()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(folder.Combine(Marker));

        Assert.Equal(InstallInfo.Portable, new FileInstallInfo(folder.Combine("Snapdate.exe")).Current);
    }

    [Fact]
    public void Marker_in_another_folder_is_ignored()
    {
        using var folder = new TempFolder();
        folder.Write(Path.Combine("other", Marker), "[Install]\r\nScope=user\r\n"u8.ToArray());

        Assert.Equal(InstallInfo.Portable, new FileInstallInfo(folder.Combine("Snapdate.exe")).Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Snapdate.exe")]
    public void Unknown_exe_location_means_portable(string? exePath)
    {
        Assert.Equal(InstallInfo.Portable, new FileInstallInfo(exePath).Current);
    }
}
