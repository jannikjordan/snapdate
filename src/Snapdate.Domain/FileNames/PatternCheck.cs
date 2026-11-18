namespace Snapdate.Domain.FileNames;

/// <summary>
/// The outcome of trying one pattern on one file name, with enough detail for an editor to explain it.
/// </summary>
/// <param name="Kind">What was found.</param>
/// <param name="Date">The date for <see cref="PatternCheckKind.Date"/> and <see cref="PatternCheckKind.ImplausibleDate"/>; otherwise null.</param>
/// <param name="Found">
/// The numbers the pattern read, as <c>YYYY-MM-DD</c> with <c>HH:mm[:ss]</c> when there is a time
/// (e.g. <c>2024-13-45</c>); empty for <see cref="PatternCheckKind.NoMatch"/>.
/// </param>
public sealed record PatternCheck(PatternCheckKind Kind, FileNameDate? Date, string Found)
{
    /// <summary>The pattern does not fit.</summary>
    public static PatternCheck NoMatch { get; } = new(PatternCheckKind.NoMatch, null, string.Empty);
}
