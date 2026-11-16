namespace Snapdate.Application.Installation;

/// <summary>How this copy of Snapdate got onto the PC.</summary>
public enum InstallKind
{
    /// <summary>A single exe run from wherever it was saved (also the answer whenever the install cannot be proven).</summary>
    Portable,

    /// <summary>Put there by the Snapdate installer.</summary>
    Installed,
}
