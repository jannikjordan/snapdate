namespace Snapdate.Domain.Naming;

/// <summary>Why a pattern cannot be used. Shown to the user in plain language by the UI.</summary>
public enum PatternErrorKind
{
    Empty,
    UnknownPlaceholder,
    UnclosedBrace,
    UnexpectedClosingBrace,
    InvalidCharacter,
    EmptyFolderName,
}

/// <param name="Kind">What is wrong.</param>
/// <param name="Position">0-based index in the pattern text.</param>
/// <param name="Detail">The offending placeholder name or character, if any.</param>
public sealed record PatternError(PatternErrorKind Kind, int Position, string? Detail = null);
