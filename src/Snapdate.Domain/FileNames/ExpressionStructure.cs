namespace Snapdate.Domain.FileNames;

/// <summary>
/// Structural limits for user-written regular expressions (<see cref="DateExpression"/>). <c>NonBacktracking</c> runs in
/// time linear in the name, but the factor depends on the expression: ambiguous repetitions next to each other (e.g.
/// <c>(?:.*a.{0,99}){0,10}</c>) can take seconds and hundreds of MB for one 260-character name, and the engine checks its
/// timeout only now and then (not at all while it fills in the groups). These limits keep every accepted expression
/// small enough that a match stays far below the timeout, so the timeout is a last resort, not the protection.
/// </summary>
/// <remarks>
/// Runs on text that already compiled, so it only has to understand valid syntax. Where it reads the text differently
/// from the engine, it errs towards seeing more structure (a character class it ends too early, an extra group);
/// groups that do not balance reject the expression.
/// </remarks>
internal static class ExpressionStructure
{
    /// <summary>At most this many quantifiers (<c>* + ? {n} {n,} {n,m}</c>, lazy or not).</summary>
    public const int MaxQuantifiers = 30;

    /// <summary>At most this many unbounded quantifiers (<c>* + {n,}</c>).</summary>
    public const int MaxUnbounded = 8;

    /// <summary>
    /// Unbounded quantifiers × the summed upper bounds of all bounded ones (<c>?</c> counts 1, <c>{n}</c> and
    /// <c>{n,m}</c> count n or m) may be at most this. Each unbounded repetition can make every bounded one ambiguous,
    /// which is what makes the engine slow; bounded repetitions alone, or unbounded ones alone, are cheap.
    /// </summary>
    public const int MaxComplexity = 160;

    /// <summary>The first structural problem, or null when the expression is within the limits.</summary>
    public static DatePatternError? Check(string text)
    {
        var groups = new Stack<Group>();
        var current = new Group(0);
        var quantifiers = 0;
        var unbounded = 0;
        var i = 0;

        // What a quantifier right here would apply to: an atom (Single), a closed group, or nothing.
        Group? lastGroup = null;
        var canQuantify = false;

        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
            {
                case '\\':
                    i = Math.Min(i + 2, text.Length);
                    lastGroup = null;
                    canQuantify = true;
                    continue;
                case '[':
                    i = SkipClass(text, i);
                    lastGroup = null;
                    canQuantify = true;
                    continue;
                case '(':
                    var open = i;
                    if (!TrySkipGroupStart(text, ref i, out var isGroup, out var freeSpacing))
                    {
                        return new DatePatternError(DatePatternErrorKind.InvalidExpression, open);
                    }

                    if (freeSpacing)
                    {
                        return new DatePatternError(DatePatternErrorKind.FreeSpacing, open);
                    }

                    if (isGroup)
                    {
                        groups.Push(current);
                        current = new Group(open);
                        lastGroup = null;
                        canQuantify = false;
                    }
                    else
                    {
                        // Inline options such as (?i) or a (?#comment): nothing to repeat.
                        lastGroup = null;
                        canQuantify = false;
                    }

                    continue;
                case ')':
                    if (groups.Count == 0)
                    {
                        return new DatePatternError(DatePatternErrorKind.InvalidExpression, i);
                    }

                    var closed = current;
                    current = groups.Pop();
                    current.HasQuantifier |= closed.HasQuantifier;
                    current.Bounded += closed.Bounded;
                    lastGroup = closed;
                    canQuantify = true;
                    i++;
                    continue;
                case '*' or '+' or '?' or '{':
                    if (!TryReadQuantifier(text, i, out var end, out var max, out var variable))
                    {
                        // A '{' that is no quantifier is a literal.
                        lastGroup = null;
                        canQuantify = true;
                        i++;
                        continue;
                    }

                    if (canQuantify)
                    {
                        if (lastGroup is { HasQuantifier: true })
                        {
                            return new DatePatternError(DatePatternErrorKind.NestedQuantifier, i, text[i..end]);
                        }

                        quantifiers++;
                        if (max is null)
                        {
                            unbounded++;
                        }
                        else
                        {
                            // A repeated group repeats what is bounded inside it, e.g. (?:\d{2}){3} counts 3 + 2 × 2.
                            var added = max.Value + ((long)Math.Max(0, max.Value - 1) * (lastGroup?.Bounded ?? 0));
                            current.Bounded = (int)Math.Min(added + current.Bounded, 100_000);
                        }

                        // A fixed count such as \d{4} only spells the atom out; it adds nothing to choose from.
                        current.HasQuantifier |= variable;
                    }

                    lastGroup = null;
                    canQuantify = false;
                    i = end;
                    continue;
                case '|' or '^' or '$':
                    lastGroup = null;
                    canQuantify = false;
                    i++;
                    continue;
                default:
                    lastGroup = null;
                    canQuantify = true;
                    i++;
                    continue;
            }
        }

        if (groups.Count > 0)
        {
            return new DatePatternError(DatePatternErrorKind.InvalidExpression, current.Start);
        }

        return quantifiers > MaxQuantifiers || unbounded > MaxUnbounded || (long)(unbounded + 1) * current.Bounded > MaxComplexity
            ? new DatePatternError(DatePatternErrorKind.TooComplex, 0)
            : null;
    }

    /// <summary>
    /// Reads a quantifier at <paramref name="start"/> (with a lazy <c>?</c>); <paramref name="max"/> is its upper bound,
    /// null when unbounded. False for a <c>{</c> that is no quantifier.
    /// </summary>
    private static bool TryReadQuantifier(string text, int start, out int end, out int? max, out bool variable)
    {
        end = start + 1;
        variable = true;
        switch (text[start])
        {
            case '*' or '+':
                max = null;
                break;
            case '?':
                max = 1;
                break;
            default:
                var j = ReadNumber(text, start + 1, out var first);
                if (j == start + 1 || j >= text.Length)
                {
                    max = null;
                    return false;
                }

                max = first;
                variable = false;
                if (text[j] == ',')
                {
                    var k = ReadNumber(text, j + 1, out var second);
                    max = k == j + 1 ? null : second;
                    variable = max != first;
                    j = k;
                }

                if (j >= text.Length || text[j] != '}')
                {
                    max = null;
                    return false;
                }

                end = j + 1;
                break;
        }

        if (end < text.Length && text[end] == '?')
        {
            end++;
        }

        return true;
    }

    private static int ReadNumber(string text, int index, out int value)
    {
        value = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            value = Math.Min(value * 10 + (text[index] - '0'), 100_000);
            index++;
        }

        return index;
    }

    /// <summary>
    /// Skips a character class starting at <paramref name="start"/> (<c>[</c>): a leading <c>^</c>, a <c>]</c> right
    /// after it is literal, escapes, and subtractions such as <c>[a-z-[aeiou]]</c>. Returns the index after it.
    /// </summary>
    private static int SkipClass(string text, int start)
    {
        var i = start + 1;
        if (i < text.Length && text[i] == '^')
        {
            i++;
        }

        if (i < text.Length && text[i] == ']')
        {
            i++;
        }

        while (i < text.Length)
        {
            switch (text[i])
            {
                case '\\':
                    i += 2;
                    continue;
                case '[' when text[i - 1] == '-':
                    i = SkipClass(text, i);
                    continue;
                case ']':
                    return i + 1;
                default:
                    i++;
                    continue;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Skips the start of a group at <paramref name="i"/> (<c>(</c>, <c>(?:</c>, <c>(?&lt;name&gt;</c>, <c>(?'name'</c>,
    /// <c>(?i:</c>) and says whether it opens a group; inline options <c>(?i)</c> and comments <c>(?#…)</c> are skipped
    /// whole. <paramref name="freeSpacing"/> is set for the <c>x</c> option.
    /// </summary>
    private static bool TrySkipGroupStart(string text, ref int i, out bool isGroup, out bool freeSpacing)
    {
        isGroup = true;
        freeSpacing = false;
        i++;
        if (i >= text.Length || text[i] != '?')
        {
            return true;
        }

        i++;
        if (i >= text.Length)
        {
            return false;
        }

        switch (text[i])
        {
            case '#':
                var close = text.IndexOf(')', i);
                isGroup = false;
                i = close < 0 ? text.Length : close + 1;
                return close >= 0;
            case '<' or '\'' when i + 1 < text.Length && text[i + 1] is not ('=' or '!'):
                var terminator = text[i] == '<' ? '>' : '\'';
                var nameEnd = text.IndexOf(terminator, i + 1);
                i = nameEnd < 0 ? text.Length : nameEnd + 1;
                return nameEnd >= 0;
            case ':' or '=' or '!' or '>':
                i++;
                return true;
            case '<':
                i += 2; // (?<= or (?<! — rejected by the engine before this runs.
                return true;
            default:
                while (i < text.Length && text[i] is 'i' or 'm' or 'n' or 's' or 'x' or '-')
                {
                    freeSpacing |= text[i] == 'x';
                    i++;
                }

                if (i >= text.Length)
                {
                    return false;
                }

                isGroup = text[i] == ':';
                i++;
                return text[i - 1] is ':' or ')';
        }
    }

    private sealed class Group(int start)
    {
        public int Start { get; } = start;

        /// <summary>True when something inside is repeated a variable number of times.</summary>
        public bool HasQuantifier { get; set; }

        /// <summary>The summed upper bounds of the bounded quantifiers inside.</summary>
        public int Bounded { get; set; }
    }
}
