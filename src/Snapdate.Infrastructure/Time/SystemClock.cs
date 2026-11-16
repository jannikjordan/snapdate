using Snapdate.Application.Ports;

namespace Snapdate.Infrastructure.Time;

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
