using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

/// <summary>
/// Accepted expressions and patterns must match quickly on hostile 260-character names. The engine's timeout is only
/// checked now and then, so these bounds are what the grammar's limits (<see cref="DateExpression"/>) guarantee. No
/// test here relies on the timeout. They run alone (never in parallel with other tests) and take the best of three
/// runs, so a busy machine does not make them fail.
/// </summary>
[Collection(nameof(TimingTests))]
public sealed class MatchTimeTests
{
    private const string Date = @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})";

    private static readonly DateTime Now = new(2026, 11, 18);

    /// <summary>Expressions from the security reviews ("D" = the three date groups). Each must be rejected or fast.</summary>
    public static TheoryData<string> ReviewCorpus() =>
    [
        @"D(?:.*a.{0,99}){0,10}",
        @"(.*a.{0,99})?(.*a.{0,99})?(.*a.{0,99})?D",
        @"D(?:.*a.{0,99}){0,3}",
        @"D(?:a*b*a*){0,100}",
        @"D(?:.*a.{0,20}){0,10}",
        @"D.*(?:(?:|.|..|...|....|.....|......|.......|........|.........)(?:|.|..|...|....|.....|......|.......|........|.........)a){20}.*",
        @"D(?:(?:|.|..|...|....|.....|......|.......|........|.........)a){20}",
        @"D.*(?:(?:|.|..|...|....)a){20}.*",
        @"D.*(?:(?:|.)a){20}.*",
        @"D.*(?:(?:|.)(?:|.)a){20}.*",
        @"D(?:(?:|.)(?:|.)(?:|.)(?:|.)a){20}.*",
        @"D[\-[x](?:.*a.{0,20}){0,20}(?:.*b.{0,20}){0,20}(?:.*a.{0,20}){0,20}]",
        @"D[\-[x](?:(?:.*a.{0,20}){0,20}){0,20}]",
        @"D[\-[x]((a{20}){20}){20}]",
        @"D[\-[x](?:.*a.{0,20}){0,20}]",
        @"D(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|).*(?:(?:|.)(?:|.)(?:|.)a){20}",
        @"D[\-[x](?:.*a.{0,20}.*a.{0,20}){0,20}]",
        @"D[\-[x](?:.*.{0,20}.{0,20}.{0,20}a){0,20}]",
        @"D[\-[x](?:.*a.{0,20}){0,20}.*(?:.*a.{0,20}){0,20}]",
        @"D[\-[x](?:.*a.{0,20}){0,20}(?:.*a.{0,20}){0,20}(?:.*a.{0,20}){0,20}(?:.*a.{0,20}){0,20}]",
        @"D[\-[x](?:.{0,20}.*.{0,20}){0,20}]",
        @"D[\-[x](?:a*b*a*){0,20}(?:a*b*a*){0,20}]",
        @"D[\-[x](?:.*a.{0,20}|.*b.{0,20}){0,20}]",
        @"D[\-[x](?:(?:|.|..|...|....|.....)a.*){0,20}]",
        @"D(?:[\-[x].*a.{0,20}]){0,20}",
        @"D(?:[\-[x].*a.{0,20}.*a.{0,20}])+",
        @"D(?:[\-[x](?:.*a.{0,20}){0,20}])",
        @"D.*a(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)",
        @"D.*(?:(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)a){20}.*",
        @"D(?:(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)a){20}",
        @"D.*(?:(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)a){20}.*(?:(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)b){20}.*",
        @"D(?:(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)){20}",
        @"D.*(?:(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)(?:a|)){20}.*",
        @"D(?:(?:.|..|...|....|.....|......)a){20}",
        @"D.*(?:(?:.|..|...|....|.....|......|.......|........)a){20}.*",
        @"D.*a....................................................................................................",
        @"D.*a..................................................................................................................................................................................................................",
        @"D.*a.........................................................................................................*a............................................................................................................",
        @"D.*a.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?",
        @"D.*a.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.*a.*",
        @"D.*a.{0,20}.*a.{0,20}.*a.{0,20}",
        @"D.*a.{0,20}.*b.{0,20}.*a.{0,20}.*b.{0,20}",
        @"D.*?a.{0,20}?.*?a.{0,20}?.*?a.{0,20}?",
        @"D[\p{L}\p{N}]*a[\p{L}\p{N}]{0,20}[\p{L}\p{N}]*a[\p{L}\p{N}]{0,20}[\p{L}\p{N}]*a[\p{L}\p{N}]{0,20}",
        @"D(?i:.*a.{0,20}.*a.{0,20}.*a.{0,20})",
        @"D(?s:.*a(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)).*a.{0,20}",
        @"D(?:.*a|.*b|.*c|.*d|.*e|.*f|.*g|.*h).{0,20}",
        @"D(?:a|b|.*)(?:a|b|.*)(?:a|b|.*)(?:a|b|.*)(?:a|b|.*)(?:a|b|.*)(?:a|b|.*)(?:a|b|.*)",
        @"D(?:(?:a|.)(?:a|.)(?:a|.)(?:a|.)(?:a|.)(?:a|.)(?:a|.)(?:a|.)(?:a|.)(?:a|.)){20}",
        @"D.*(?:.{20}){20}.*",
        @"D(?:\w|\d|\s|.)*a(?:\w|\d|\s|.){0,20}(?:\w|\d|\s|.)*a(?:\w|\d|\s|.){0,20}",
        @"D.*a.{0,20}.*a.{0,20}.*a.{0,20}.*a.{0,20}",
        @"D.+.+.+.+.+.+.+.+a.{0,17}",
        @"D(.*a.{0,20})?",
        @"D(?:.*a.{0,9})?",
        @"D(?:x?){20}",
        @"D(?:\d{2}){20}(?:\d{2}){20}",
        @"D(?:(?:|.){20}){20}",
        @"D.*(?:(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)a){20}.*(?:(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)(?:|.)a){8}.*",
        @"D[a-z]*.{0,20}[a-z]*.{0,20}[a-z]*.{0,20}[a-z]*.{0,20}[a-z]*.{0,20}[a-z]*.{0,20}",
    ];

    /// <summary>The slowest expressions found among those still accepted (fuzzing and the limits' edges).</summary>
    public static TheoryData<string> ExpensiveAcceptedExpressions() =>
    [
        Date + "[a-z]*.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,11}",
        Date + @"\w*.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,11}",
        Date + ".*a.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,10}",
        Date + ".*a.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,10}?",
        Date + ".*?a.{0,20}?.{0,20}?.{0,20}?.{0,20}?.{0,20}?.{0,10}?",
        Date + "[a-z]*.{0,20}[a-z]*.{0,20}.{0,10}",
        Date + ".*.*.*.*.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?.?",
        Date + ".*a" + new string('.', 110),
        Date + ".*a" + string.Concat(Enumerable.Repeat("(?:|.)", 35)),
        Date + "[a-z]*.{0,9}[a-z]*.{0,9}[a-z]*.{0,9}",
        Date + ".*a.{0,8}.*a.{0,8}.*a.{0,8}",
        Date + "[a-z]*(?:abcde){20}",
        Date + "(?:ab|ba)*(?:ab|ba)*(?:ab|ba)*",
        @"[\d\s_]{1,20}\d(?:.|..|...)[^_]*[^_]{20}" + Date + ".*",
        ".*" + Date + ".*",
    ];

    [Theory]
    [MemberData(nameof(ReviewCorpus))]
    public void Every_review_expression_is_rejected_or_fast(string corpus)
    {
        var text = corpus.StartsWith('D') ? Date + corpus[1..] : corpus.EndsWith('D') ? corpus[..^1] + Date : corpus;

        if (DateExpression.Parse(text).Expression is { } expression)
        {
            AssertFast(expression, text);
        }
    }

    [Theory]
    [MemberData(nameof(ExpensiveAcceptedExpressions))]
    public void Expensive_accepted_expressions_stay_within_the_bound(string text)
    {
        var result = DateExpression.Parse(text);
        Assert.True(result.IsValid, string.Join(", ", result.Errors));

        AssertFast(result.Expression!, text);
    }

    [Fact]
    public void Fuzzed_accepted_expressions_stay_within_the_bound()
    {
        // Atoms include empty and different-length alternatives, classes with escapes and "[", and fixed-count groups.
        string[] atoms =
        [
            ".", "a", "b", "[a-z]", @"\d", @"\w", "[^_]", "[^a]", @"\.", @"[\d\s_]", @"[\]a-]", @"[\-\[]", @"[\-[x]",
            "(?:a|ab)", "(?:a|.)", "(?:|.)", "(?:a|)", "(?:.|..|...)", "(?:|.|..|...|....)", "(?:..|ab)", "(?:.a)",
        ];
        string[] quantifiers = ["*", "+", "?", "*?", "{0,20}", "{1,20}", "{20}", "{0,9}", "{2,}", "{0,5}", "{3}", "{10}", "", "", "", ""];
        var random = new Random(21);

        string Piece(int depth)
        {
            if (depth < 2 && random.Next(4) == 0)
            {
                var inner = string.Concat(Enumerable.Range(0, random.Next(1, 4)).Select(_ => Piece(depth + 1)));
                return "(?:" + inner + ")" + quantifiers[random.Next(quantifiers.Length)];
            }

            return atoms[random.Next(atoms.Length)] + quantifiers[random.Next(quantifiers.Length)];
        }

        var accepted = 0;
        for (var i = 0; i < 1000; i++)
        {
            var body = string.Concat(Enumerable.Range(0, random.Next(1, 25)).Select(_ => Piece(0)));
            var text = random.Next(2) == 0 ? Date + body : body + Date + Piece(0);
            if (DateExpression.Parse(text).Expression is not { } expression)
            {
                continue;
            }

            accepted++;
            var worst = AdversarialNames.Worst(name => expression.TryMatch(name, Now, out _));
            Assert.True(worst < AdversarialNames.Bound, $"{worst.TotalMilliseconds:0} ms for {text}");
        }

        // The corpus must exercise accepted expressions, not only rejected ones.
        Assert.InRange(accepted, 30, 1000);
    }

    [Theory]
    [InlineData(93, "*a")]
    [InlineData(93, "*?")]
    [InlineData(62, "*a?")]
    [InlineData(93, "*0")]
    [InlineData(60, "*-")]
    public void Token_patterns_with_many_wildcards_stay_within_the_bound(int count, string piece)
    {
        string[] texts =
        [
            string.Concat(Enumerable.Repeat(piece, count)) + "{YYYY}{MM}{DD}",
            "{YYYY}{MM}{DD}" + string.Concat(Enumerable.Repeat(piece, count)),
            "*{YYYY}*{MM}*{DD}*" + string.Concat(Enumerable.Repeat(piece, ((count * piece.Length) - 18) / piece.Length)),
        ];

        foreach (var text in texts)
        {
            var result = DatePattern.Parse(text);
            Assert.True(result.IsValid, string.Join(", ", result.Errors));
            Assert.True(text.Length <= DatePattern.MaxLength);
            var pattern = result.Pattern!;

            var worst = AdversarialNames.Worst(name => pattern.TryMatch(name, Now, out _));

            Assert.True(worst < AdversarialNames.Bound, $"{worst.TotalMilliseconds:0} ms for {text}");
        }
    }

    [Fact]
    public void Many_regex_rules_together_stay_within_the_budget_per_name()
    {
        // 100 rules × 10 of the most expensive accepted expressions, none of which matches: without the budget this
        // would add up to seconds per name.
        string[] expensive =
        [
            Date + "x[a-z]*.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,9}",
            Date + "x.*a" + new string('.', 108),
        ];
        var rules = Enumerable.Range(0, 100)
            .Select(i => new DateRule($"custom-{i}", $"R{i}", RuleOrigin.Custom, Enabled: true, [.. Enumerable.Range(0, 10).Select(j => expensive[j % 2])], DateRuleSyntax.Regex))
            .ToList();
        var set = new DateRuleSet(rules);
        Assert.All(rules, rule => Assert.All(rule.Patterns, p => Assert.True(DateExpression.Parse(p).IsValid, p)));

        var worst = AdversarialNames.Worst(name => set.Match(name, Now));

        // The budget plus at most one expression that was already running when it ran out.
        Assert.True(worst < DateRuleSet.DefaultRegexBudget + AdversarialNames.Bound, worst.ToString());
    }

    private static void AssertFast(DateExpression expression, string text)
    {
        var worstMatch = AdversarialNames.Worst(name => expression.TryMatch(name, Now, out _));
        var worstCheck = AdversarialNames.Worst(name => expression.Check(name, Now));

        Assert.True(worstMatch < AdversarialNames.Bound, $"{worstMatch.TotalMilliseconds:0} ms for {text}");
        Assert.True(worstCheck < AdversarialNames.Bound, $"{worstCheck.TotalMilliseconds:0} ms for {text}");
    }
}
