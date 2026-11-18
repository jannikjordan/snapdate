namespace Snapdate.App.ViewModels;

/// <summary>The kind of result the rule editor's tester shows; the view picks icon and colour from it.</summary>
public enum RuleSampleKind
{
    /// <summary>No name typed, or no valid pattern to try.</summary>
    Empty,

    /// <summary>A pattern reads a valid date.</summary>
    Match,

    /// <summary>A pattern fits, but the numbers are no date (or one before 1900 or in the future).</summary>
    NotADate,

    /// <summary>No pattern fits.</summary>
    NoMatch,
}
