using Snapdate.Domain.FileNames;

namespace Snapdate.Domain.Tests.FileNames;

/// <summary>
/// Accepted expressions and patterns must match quickly on hostile 260-character names. The engine's timeout is only
/// checked now and then, so these bounds are what the structural limits of <see cref="DateExpression"/> guarantee.
/// No test here relies on the timeout.
/// </summary>
public sealed class MatchTimeTests
{
    private const string Date = @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})";

    private static readonly DateTime Now = new(2026, 11, 18);

    /// <summary>The slowest expressions found among those still accepted (fuzzing and the limits' edges).</summary>
    public static TheoryData<string> ExpensiveAcceptedExpressions() =>
    [
        Date + @"(?:(?:a|ab){0,20})\w{0,20}(?:a{0,9}\w*?)(?:a|ab){0,20}",
        Date + @".{0,9}(?:a|.){0,9}[a-z]{0,20}.?(?:a|ab){1,20}\w+",
        Date + @"(?:a|.)?(?:a|ab){0,9}\w?[^_]{3}(?:.*[a-z]{1,20}[a-z]{0,9})(?:a|ab){1,20}[a-z]{0,9}",
        Date + ".*a.*a.*a.*a.*a.*a.*a.*a.?.?.?.?.?.?.?.?.?",
        Date + ".{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,20}.{0,4}",
        Date + "[a-z]*.{0,20}.{0,20}.{0,20}.{0,12}",
        ".*a.{0,20}.{0,20}.{0,20}.{0,12}" + Date,
        Date + "(?:a|ab|ba|b)*(?:a|ab|ba|b)*(?:a|ab|ba|b)*(?:a|ab|ba|b)*(?:a|ab|ba|b)*(?:a|ab|ba|b)*(?:a|ab|ba|b)*(?:a|ab|ba|b)*",
        Date + "[a-z]*(?:x.{5}){10}",
        ".*" + Date + ".*",
    ];

    [Theory]
    [MemberData(nameof(ExpensiveAcceptedExpressions))]
    public void Expensive_accepted_expressions_stay_within_the_bound(string text)
    {
        var result = DateExpression.Parse(text);
        Assert.True(result.IsValid, string.Join(", ", result.Errors));
        var expression = result.Expression!;

        var worst = AdversarialNames.Worst(name =>
        {
            expression.TryMatch(name, Now, out _);
            expression.Check(name, Now);
        });

        // Two matches per name (TryMatch and Check).
        Assert.True(worst < 2 * AdversarialNames.Bound, $"{worst.TotalMilliseconds:0} ms for {text}");
    }

    [Fact]
    public void Fuzzed_accepted_expressions_stay_within_the_bound()
    {
        string[] atoms = [".", "a", "[a-z]", @"\d", "[^_]", "(?:a|ab)", "(?:a|.)", @"\w", "b"];
        string[] quantifiers = ["*", "+", "?", "*?", "{0,20}", "{1,20}", "{20}", "{0,9}", "{2,}", "{0,5}", "{3}", "", "", ""];
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
        for (var i = 0; i < 600; i++)
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
        Assert.InRange(accepted, 40, 600);
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
            "*{YYYY}*{MM}*{DD}*" + string.Concat(Enumerable.Repeat(piece, (count * piece.Length - 18) / piece.Length)),
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
}
