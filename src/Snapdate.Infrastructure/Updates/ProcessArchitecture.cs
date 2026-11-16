using System.Runtime.InteropServices;
using Snapdate.Application.Ports;
using Snapdate.Application.Updates;

namespace Snapdate.Infrastructure.Updates;

/// <summary>Maps the runtime's process architecture to the installers we ship (x64, arm64).</summary>
public sealed class ProcessArchitecture : IProcessArchitecture
{
    public UpdateArchitecture Current => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => UpdateArchitecture.X64,
        Architecture.Arm64 => UpdateArchitecture.Arm64,
        _ => UpdateArchitecture.Unsupported,
    };
}
