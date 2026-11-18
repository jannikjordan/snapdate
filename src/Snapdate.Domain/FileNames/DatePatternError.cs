namespace Snapdate.Domain.FileNames;

/// <param name="Kind">What is wrong.</param>
/// <param name="Position">0-based index in the pattern text.</param>
/// <param name="Detail">The offending placeholder name, if any (without braces).</param>
public sealed record DatePatternError(DatePatternErrorKind Kind, int Position, string? Detail = null);
