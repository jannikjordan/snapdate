namespace Snapdate.Domain.FileNames;

/// <summary>
/// Rules in precedence order, compiled once. <see cref="Match"/> returns the first enabled rule whose
/// pattern gives a valid date. Disabled rules, rules in an unsupported syntax and invalid patterns are
/// skipped (a rule with one invalid pattern still uses its valid ones).
/// </summary>
public sealed class DateRuleSet
{
    /// <summary>
    /// Time that regular-expression rules may take together for one name. Once spent, further regex rules are skipped
    /// for that name (token patterns still run: they are cheap). Keeps 100 rules × 10 expressions from adding up.
    /// </summary>
    public static readonly TimeSpan DefaultRegexBudget = TimeSpan.FromMilliseconds(200);

    private readonly IReadOnlyList<CompiledRule> _compiled;
    private readonly TimeSpan _regexBudget;

    /// <param name="rules">Rules in precedence order (custom rules first, then system rules).</param>
    public DateRuleSet(IEnumerable<DateRule> rules)
        : this(rules, DefaultRegexBudget)
    {
    }

    /// <param name="rules">Rules in precedence order (custom rules first, then system rules).</param>
    /// <param name="regexBudget">See <see cref="DefaultRegexBudget"/>.</param>
    internal DateRuleSet(IEnumerable<DateRule> rules, TimeSpan regexBudget)
    {
        _regexBudget = regexBudget;
        Rules = [.. (rules ?? []).Where(rule => rule is not null)];
        _compiled = [.. Rules.Where(rule => rule.Enabled).Select(Compile).Where(rule => rule.Patterns.Count > 0)];
    }

    /// <summary>A set without rules; never matches.</summary>
    public static DateRuleSet Empty { get; } = new([]);

    /// <summary>All rules as given (including disabled and unsupported ones), in precedence order.</summary>
    public IReadOnlyList<DateRule> Rules { get; }

    /// <summary>
    /// Reads a date from <paramref name="name"/> (file name without extension). Never throws;
    /// null when no rule gives a valid, plausible date. Regular-expression rules share a time budget per name
    /// (<see cref="DefaultRegexBudget"/>); once it is spent they are skipped.
    /// </summary>
    public DateRuleMatch? Match(string? name, DateTime localNow)
    {
        if (string.IsNullOrEmpty(name) || name.Length > DatePattern.MaxNameLength)
        {
            return null;
        }

        var regexTime = TimeSpan.Zero;
        foreach (var rule in _compiled)
        {
            foreach (var (index, matcher) in rule.Patterns)
            {
                var isRegex = matcher is DateExpression;
                if (isRegex && regexTime >= _regexBudget)
                {
                    continue;
                }

                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var found = matcher.TryMatch(name, localNow, out var date);
                if (isRegex)
                {
                    regexTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
                }

                if (found)
                {
                    return new DateRuleMatch(date, rule.Rule.Id, rule.Rule.Name, index);
                }
            }
        }

        return null;
    }

    private static CompiledRule Compile(DateRule rule)
    {
        var patterns = new List<(int Index, IFileNameDateMatcher Matcher)>();
        var texts = rule.Patterns ?? [];
        for (var i = 0; i < texts.Count && i < DateRule.MaxPatterns; i++)
        {
            if (CompilePattern(rule.Syntax, texts[i]) is { } matcher)
            {
                patterns.Add((i, matcher));
            }
        }

        return new CompiledRule(rule, patterns);
    }

    // One case per supported syntax.
    private static IFileNameDateMatcher? CompilePattern(string syntax, string? text) => syntax switch
    {
        DateRuleSyntax.Pattern => DatePattern.Parse(text).Pattern,
        DateRuleSyntax.Regex => DateExpression.Parse(text).Expression,
        _ => null,
    };

    private sealed record CompiledRule(DateRule Rule, IReadOnlyList<(int Index, IFileNameDateMatcher Matcher)> Patterns);
}
