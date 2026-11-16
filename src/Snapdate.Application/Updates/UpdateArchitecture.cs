namespace Snapdate.Application.Updates;

/// <summary>The CPU architecture of the running Snapdate process; selects the matching installer.</summary>
public enum UpdateArchitecture
{
    /// <summary>Neither x64 nor ARM64: no installer is offered.</summary>
    Unsupported,

    X64,

    Arm64,
}
