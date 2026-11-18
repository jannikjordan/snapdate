namespace Snapdate.Domain.FileNames;

/// <summary>A date read from a file name; the time is only known when the name contains one.</summary>
/// <param name="Date">The calendar day.</param>
/// <param name="Time">The time of day, or null when the name only gives the day.</param>
public readonly record struct FileNameDate(DateOnly Date, TimeOnly? Time)
{
    /// <summary>The date and time, with 00:00:00 when the name has no time.</summary>
    public DateTime ToDateTime() => Date.ToDateTime(Time ?? TimeOnly.MinValue);
}
