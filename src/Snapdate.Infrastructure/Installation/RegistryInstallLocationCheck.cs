using Microsoft.Win32;
using Snapdate.Application.Installation;
using Snapdate.Application.Ports;

namespace Snapdate.Infrastructure.Installation;

/// <summary>
/// Confirms the install marker with the installer's uninstall entry: the <c>InstallLocation</c> of
/// <c>...\Uninstall\{AppId}_is1</c> (HKCU for a per-user install, HKLM for all users) must be the folder of the
/// running exe. Someone who can only write next to the exe cannot forge this for an all-users install, and for
/// a per-user install it raises the bar beyond a text file. See docs/security.md.
/// </summary>
public sealed class RegistryInstallLocationCheck : IInstallLocationCheck
{
    internal const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{D5F11F70-E97E-44D0-8573-E15843FA60B3}_is1";

    private readonly string? _exeFolder;
    private readonly Func<InstallScope, IReadOnlyList<string>> _readLocations;

    /// <summary>Production constructor: the folder of the running exe and the real registry.</summary>
    public RegistryInstallLocationCheck()
        : this(Path.GetDirectoryName(Environment.ProcessPath), ReadFromRegistry)
    {
    }

    internal RegistryInstallLocationCheck(string? exeFolder, Func<InstallScope, IReadOnlyList<string>> readLocations)
    {
        _exeFolder = exeFolder;
        _readLocations = readLocations;
    }

    public bool IsRegisteredForRunningExe(InstallScope scope)
    {
        if (scope is not (InstallScope.CurrentUser or InstallScope.AllUsers) || string.IsNullOrWhiteSpace(_exeFolder))
        {
            return false;
        }

        try
        {
            var exe = Normalize(_exeFolder);
            return exe is not null && _readLocations(scope).Any(l => string.Equals(Normalize(l), exe, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Full path without trailing separator, or null when the text is not a usable absolute path.</summary>
    internal static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var trimmed = path.Trim().Trim('"');
            return Path.IsPathFullyQualified(trimmed) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed)) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadFromRegistry(InstallScope scope)
    {
        var results = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            return results;
        }

        var hive = scope == InstallScope.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(UninstallKeyPath, writable: false);
                if (key?.GetValue("InstallLocation") is string location)
                {
                    results.Add(location);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Unreadable view: treated as "not registered".
            }
        }

        return results;
    }
}
