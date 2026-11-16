namespace Snapdate.Domain.Naming;

/// <summary>Every placeholder a name pattern may contain.</summary>
public enum TokenKind
{
    Year4,
    Year2,
    Month2,
    Month1,
    MonthNameShort,
    MonthNameLong,
    Day2,
    Day1,
    WeekdayShort,
    WeekdayLong,
    Hour2,
    Hour1,
    Minute2,
    Minute1,
    Second2,
    Second1,
    OriginalName,
    Camera,
    Counter,
}
