namespace Snapdate.Domain.FileNames;

/// <summary>The result of <see cref="DateRuleSet.Match"/>: the date and which rule produced it.</summary>
/// <param name="Date">The date (and time, if any) read from the name.</param>
/// <param name="RuleId">Id of the matching rule.</param>
/// <param name="RuleName">Display name of the matching rule.</param>
/// <param name="PatternIndex">0-based index of the matching pattern in <see cref="DateRule.Patterns"/>.</param>
public sealed record DateRuleMatch(FileNameDate Date, string RuleId, string RuleName, int PatternIndex);
