using Snapdate.Application.Installation;
using Snapdate.Application.Ports;

namespace Snapdate.Infrastructure.Installation;

/// <summary>
/// Detects an installed copy by the marker file <c>snapdate-install.ini</c> that the installer writes next
/// to <c>Snapdate.exe</c>. A missing, unreadable or malformed marker means portable.
/// </summary>
/// <remarks>
/// The marker is a hint for the UI (for example which update path to offer), not a security boundary:
/// anyone who can write next to the exe can forge it, and every downloaded update is signature-verified
/// regardless.
/// </remarks>
public sealed class FileInstallInfo : IInstallInfo
{
    public FileInstallInfo(string? exePath)
    {
        Current = Read(exePath);
    }

    public InstallInfo Current { get; }

    /// <summary>Looks next to <see cref="Environment.ProcessPath"/>.</summary>
    public static FileInstallInfo ForCurrentProcess() => new(Environment.ProcessPath);

    private static InstallInfo Read(string? exePath)
    {
        try
        {
            var directory = string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(directory))
            {
                return InstallInfo.Portable;
            }

            var info = new FileInfo(Path.Combine(directory, InstallMarker.FileName));
            if (!info.Exists || info.Length > InstallMarker.MaxBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return InstallInfo.Portable;
            }

            var scope = InstallMarker.Parse(File.ReadAllBytes(info.FullName));
            return scope is { } s ? new InstallInfo(InstallKind.Installed, s) : InstallInfo.Portable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return InstallInfo.Portable;
        }
    }
}
