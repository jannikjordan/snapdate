namespace Snapdate.Domain.FileNames;

/// <summary>A compiled pattern of any <see cref="DateRuleSyntax"/>.</summary>
internal interface IFileNameDateMatcher
{
    bool TryMatch(string? name, DateTime localNow, out FileNameDate date);
}
