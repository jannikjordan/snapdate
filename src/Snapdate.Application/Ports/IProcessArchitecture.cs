using Snapdate.Application.Updates;

namespace Snapdate.Application.Ports;

/// <summary>The architecture the running process was built for.</summary>
public interface IProcessArchitecture
{
    UpdateArchitecture Current { get; }
}
