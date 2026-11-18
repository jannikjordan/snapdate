using System.Globalization;
using System.Text;
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
        var check = Check(match, localNow);
        date = check.Kind == PatternCheckKind.Date ? check.Date!.Value : default;
        return check.Kind == PatternCheckKind.Date;
    }

    /// <summary>Reads the date of a successful match and says whether it is a valid, plausible date.</summary>
    public static PatternCheck Check(Match match, DateTime localNow)
    {
        var found = Describe(match, localNow);
        if (!TryReadYear(match, localNow, out var year)
            || !TryRead(match, Month, out var month)
            || !TryRead(match, Day, out var day)
            || year is < 1 or > 9999
            || month is < 1 or > 12
            || day < 1
            || day > DateTime.DaysInMonth(year, month))
        {
            return new PatternCheck(PatternCheckKind.InvalidDate, null, found);
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
                return new PatternCheck(PatternCheckKind.InvalidDate, null, found);
            }

            time = new TimeOnly(hour, minute, second);
        }

        var candidate = new FileNameDate(new DateOnly(year, month, day), time);
        return DatePlausibility.IsPlausible(candidate.ToDateTime(), localNow)
            ? new PatternCheck(PatternCheckKind.Date, candidate, found)
            : new PatternCheck(PatternCheckKind.ImplausibleDate, candidate, found);
    }

    /// <summary>The captured numbers as <c>YYYY-MM-DD[ HH:mm[:ss]]</c>; a two-digit year is shown as the year it stands for.</summary>
    private static string Describe(Match match, DateTime localNow)
    {
        var text = new StringBuilder();
        text.Append(TryReadYear(match, localNow, out var year) && year is >= 0 and <= 9999
            ? year.ToString("0000", CultureInfo.InvariantCulture)
            : Raw(match, Year) ?? Raw(match, TwoDigitYear) ?? "?");
        text.Append('-').Append(Raw(match, Month) ?? "?");
        text.Append('-').Append(Raw(match, Day) ?? "?");
        if (Raw(match, Hour) is { } hour)
        {
            text.Append(' ').Append(hour).Append(':').Append(Raw(match, Minute) ?? "00");
            if (Raw(match, Second) is { } second)
            {
                text.Append(':').Append(second);
            }
        }

        return text.ToString();
    }

    /// <summary>A captured value for display; long captures (possible with user-written expressions) are shortened.</summary>
    private static string? Raw(Match match, string group) =>
        match.Groups[group] is { Success: true } captured
            ? captured.Length <= 4 ? captured.Value : string.Concat(captured.ValueSpan[..4], "…")
            : null;

    private static bool TryReadYear(Match match, DateTime localNow, out int year)
    {
        if (match.Groups[Year].Success)
        {
            return TryRead(match, Year, out year);
        }

        if (TryRead(match, TwoDigitYear, out var twoDigits) && twoDigits <= 99)
        {
            // 20YY, unless that year has not begun yet: then 19YY. The pivot compares whole years only, so a
            // date later in the current year (e.g. 26-12-31 on 2026-11-18) stays 2026 and is then rejected
            // by DatePlausibility: no match, rather than a guess of 1926.
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
