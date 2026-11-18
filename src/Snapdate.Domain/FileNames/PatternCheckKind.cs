namespace Snapdate.Domain.FileNames;

/// <summary>What <see cref="DatePattern.Check"/> found when it tried a pattern on a file name.</summary>
public enum PatternCheckKind
{
    /// <summary>The pattern does not fit the name.</summary>
    NoMatch,

    /// <summary>The pattern fits and gives a valid, plausible date.</summary>
    Date,

    /// <summary>The pattern fits, but the numbers are no calendar date or time (e.g. month 13).</summary>
    InvalidDate,

    /// <summary>The pattern fits and gives a real date, but one before 1900 or in the future.</summary>
    ImplausibleDate,
}
