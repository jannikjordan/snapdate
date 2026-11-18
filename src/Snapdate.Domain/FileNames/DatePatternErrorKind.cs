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
    /// The digits could be split in more than one way: two placeholders without leading zero in one run of
    /// adjacent placeholders (e.g. <c>{M}{YYYY}{D}</c>), <c>*</c> next to a run that contains one
    /// (e.g. <c>{YYYY}{M}{DD}*</c>), or <c>?</c> inside such a run (e.g. <c>{YYYY}{M}{DD}?-*</c>).
    /// </summary>
    AmbiguousNeighbours,

    /// <summary>
    /// The pattern could not be compiled into an expression. Not expected for token patterns; used by
    /// syntaxes whose expressions the user writes when no more specific kind applies.
    /// </summary>
    InvalidExpression,

    /// <summary>
    /// Regular expressions: not a valid expression (e.g. a missing <c>)</c>); <see cref="DatePatternError.Position"/>
    /// is where the problem was found.
    /// </summary>
    InvalidSyntax,

    /// <summary>
    /// Regular expressions: a construct the safe (non-backtracking) matcher cannot run — backreferences,
    /// lookarounds, atomic groups, conditionals, <c>\G</c>, balancing groups — or an expression that is too
    /// complex overall.
    /// </summary>
    UnsupportedConstruct,

    /// <summary>
    /// Regular expressions: a counted repetition above <see cref="DateExpression.MaxRepetition"/>;
    /// <see cref="DatePatternError.Detail"/> is the repetition as written, e.g. <c>{500}</c>.
    /// </summary>
    RepetitionTooLarge,

    /// <summary>
    /// Regular expressions: a named group that is not a date part; <see cref="DatePatternError.Detail"/> is its name.
    /// </summary>
    UnknownGroup,

    /// <summary>
    /// Regular expressions: a group that contains a variable repetition is repeated again, e.g. <c>(?:.*a)+</c> or
    /// <c>(\d+)?</c>; <see cref="DatePatternError.Detail"/> is the outer quantifier as written.
    /// </summary>
    NestedQuantifier,

    /// <summary>
    /// Regular expressions: too many repetitions, or too many unbounded ones (<c>* + {n,}</c>) combined with bounded
    /// ones, to run safely (see <see cref="DateExpression"/>).
    /// </summary>
    TooComplex,

    /// <summary>Regular expressions: the <c>x</c> option (white space ignored, <c>#</c> comments) is not supported.</summary>
    FreeSpacing,
}
