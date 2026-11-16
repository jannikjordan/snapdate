namespace Snapdate.Application.Installation;

/// <summary>Whether and how Snapdate was installed.</summary>
/// <param name="Kind">Installed by the installer, or portable.</param>
/// <param name="Scope">Always <see cref="InstallScope.None"/> for <see cref="InstallKind.Portable"/>.</param>
public sealed record InstallInfo(InstallKind Kind, InstallScope Scope)
{
    /// <summary>The answer for a portable exe.</summary>
    public static InstallInfo Portable { get; } = new(InstallKind.Portable, InstallScope.None);
}
