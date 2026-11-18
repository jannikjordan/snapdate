namespace Snapdate.Domain.FileNames;

/// <summary>A compiled pattern of any <see cref="DateRuleSyntax"/>.</summary>
internal interface IFileNameDateMatcher
{
    bool TryMatch(string? name, DateTime localNow, out FileNameDate date);

    /// <summary>Like <see cref="TryMatch"/>, with the reason when a fitting name gives no date.</summary>
    PatternCheck Check(string? name, DateTime localNow);
}
