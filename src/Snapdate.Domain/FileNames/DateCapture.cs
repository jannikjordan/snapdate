using System.Globalization;
using System.Text.RegularExpressions;
using Snapdate.Domain.Photos;

namespace Snapdate.Domain.FileNames;

/// <summary>
/// Turns the named groups of a successful match (<c>year</c> or <c>yy</c>, <c>month</c>, <c>day</c>,
/// <c>hour</c>, <c>minute</c>, <c>second</c>) into a validated, plausible <see cref="FileNameDate"/>.
/// Shared by every <see cref="DateRuleSyntax"/>.
/// </summary>
internal static class DateCapture
{
    public const string Year = "year";
    public const string TwoDigitYear = "yy";
    public const string Month = "month";
    public const string Day = "day";
    public const string Hour = "hour";
    public const string Minute = "minute";
    public const string Second = "second";

    public static bool TryRead(Match match, DateTime localNow, out FileNameDate date)
    {
        date = default;

        if (!TryReadYear(match, localNow, out var year)
            || !TryRead(match, Month, out var month)
            || !TryRead(match, Day, out var day)
            || year is < 1 or > 9999
            || month is < 1 or > 12
            || day < 1
            || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        TimeOnly? time = null;
        if (match.Groups[Hour].Success)
        {
            if (!TryRead(match, Hour, out var hour)
                || !TryReadOptional(match, Minute, out var minute)
                || !TryReadOptional(match, Second, out var second)
                || hour > 23
                || minute > 59
                || second > 59)
            {
                return false;
            }

            time = new TimeOnly(hour, minute, second);
        }

        var candidate = new FileNameDate(new DateOnly(year, month, day), time);
        if (!DatePlausibility.IsPlausible(candidate.ToDateTime(), localNow))
        {
            return false;
        }

        date = candidate;
        return true;
    }

    private static bool TryReadYear(Match match, DateTime localNow, out int year)
    {
        if (match.Groups[Year].Success)
        {
            return TryRead(match, Year, out year);
        }

        if (TryRead(match, TwoDigitYear, out var twoDigits) && twoDigits <= 99)
        {
            // 20YY, unless that year has not begun yet: then 19YY.
            year = 2000 + twoDigits > localNow.Year ? 1900 + twoDigits : 2000 + twoDigits;
            return true;
        }

        year = 0;
        return false;
    }

    private static bool TryReadOptional(Match match, string group, out int value)
    {
        if (!match.Groups[group].Success)
        {
            value = 0;
            return true;
        }

        return TryRead(match, group, out value);
    }

    private static bool TryRead(Match match, string group, out int value)
    {
        value = 0;
        var captured = match.Groups[group];
        if (!captured.Success)
        {
            return false;
        }

        var digits = captured.ValueSpan;
        if (digits.IsEmpty || digits.Length > 4)
        {
            return false;
        }

        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
