namespace Snapdate.Domain.FileNames;

/// <summary>Why a file-name date pattern cannot be used. Shown to the user in plain language by the UI.</summary>
public enum DatePatternErrorKind
{
    /// <summary>The pattern is empty or only white space.</summary>
    Empty,

    /// <summary>The pattern is longer than <see cref="DatePattern.MaxLength"/> characters.</summary>
    TooLong,

    /// <summary>A <c>{</c> has no matching <c>}</c>.</summary>
    UnclosedBrace,

    /// <summary>A single <c>}</c> without an opening brace (write <c>}}</c> for a literal brace).</summary>
    UnexpectedClosingBrace,

    /// <summary>The text in braces is not a placeholder at all.</summary>
    UnknownToken,

    /// <summary>A name-builder placeholder that cannot be read back from a name, e.g. <c>{MMM}</c> or <c>{name}</c>.</summary>
    UnsupportedToken,

    /// <summary>The same date part appears twice (e.g. <c>{YYYY}</c> and <c>{YY}</c>).</summary>
    DuplicateToken,

    /// <summary>No year placeholder.</summary>
    MissingYear,

    /// <summary>No month placeholder.</summary>
    MissingMonth,

    /// <summary>No day placeholder.</summary>
    MissingDay,

    /// <summary>An hour without minutes.</summary>
    HourWithoutMinute,

    /// <summary>Minutes without an hour.</summary>
    MinuteWithoutHour,

    /// <summary>Seconds without minutes.</summary>
    SecondWithoutMinute,

    /// <summary>
    /// <c>*</c> or <c>?</c> directly next to a placeholder without leading zero (e.g. <c>*{M}</c>), or two such
    /// placeholders next to each other (e.g. <c>{M}{D}</c>): the digits could be split in more than one way.
    /// </summary>
    AmbiguousNeighbours,
}
