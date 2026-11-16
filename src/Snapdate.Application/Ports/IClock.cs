namespace Snapdate.Application.Ports;

/// <summary>Source of the current time; replaceable in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
