namespace Snapdate.Domain.Photos;

/// <summary>
/// Rejects dates that cannot be a real capture moment: unset camera clocks (before 1900) and
/// dates more than <see cref="FutureTolerance"/> after now (allows for time-zone differences).
/// </summary>
public static class DatePlausibility
{
    /// <summary>The earliest accepted date: 1900-01-01 00:00.</summary>
    public static DateTime Earliest { get; } = new(1900, 1, 1);

    /// <summary>How far after now a date may lie.</summary>
    public static TimeSpan FutureTolerance { get; } = TimeSpan.FromDays(2);

    /// <summary>
    /// True when <paramref name="value"/> lies between <see cref="Earliest"/> and
    /// <paramref name="localNow"/> + <see cref="FutureTolerance"/> (both inclusive).
    /// <see cref="DateTime.Kind"/> is ignored; both values are treated as local wall-clock time.
    /// </summary>
    public static bool IsPlausible(DateTime value, DateTime localNow) =>
        value >= Earliest && value <= Latest(localNow);

    private static DateTime Latest(DateTime localNow) =>
        localNow > DateTime.MaxValue - FutureTolerance ? DateTime.MaxValue : localNow + FutureTolerance;
}
