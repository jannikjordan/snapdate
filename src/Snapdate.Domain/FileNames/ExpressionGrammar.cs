namespace Snapdate.Domain.FileNames;

/// <summary>
/// The subset of regular expressions that <see cref="DateExpression"/> accepts, read by a small recursive-descent
/// parser. Snapdate decides what is accepted; the regex engine compiles the same text afterwards and must agree.
/// </summary>
/// <remarks>
/// <para>Grammar (white space is a literal character):</para>
/// <code>
/// expression  = branch { "|" branch }
/// branch      = { piece }
/// piece       = atom [ quantifier ]
/// atom        = literal | "\" escape | "." | class | group | "^" | "$"
/// literal     = any character except \ . [ ] ( ) { } * + ? | ^ $ and control characters
/// escape      = "d" | "s" | "w" | any character that is not a letter, digit, "_" or control character
/// class       = "[" [ "^" ] item { item } "]"
/// item        = char [ "-" char ] | "\d" | "\s" | "\w"
/// char        = any character except \ [ ] and control characters | "\" escape
/// group       = "(" expression ")" | "(?:" expression ")" | "(?&lt;" name "&gt;" expression ")"
/// name        = year | yy | month | day | hour | minute | second
/// quantifier  = ( "*" | "+" | "?" | "{" n "}" | "{" n ",}" | "{" n "," m "}" ) [ "?" ]       n ≤ m ≤ 20
/// </code>
/// <para>
/// Speed: <c>NonBacktracking</c> takes time linear in the name, with a factor that grows with how many places of the
/// expression can be active at once. Width that varies — repetitions, and alternatives of different length such as
/// <c>(?:|.)</c> — next to unbounded repetitions is what drives it up. So every node knows its shortest and longest
/// width, a quantifier on anything whose width varies (or that contains such a part) is rejected
/// (<c>(?:.*a)+</c>, <c>(?:x?){3}</c>, <c>(?:|.)*</c>), and a budget limits unbounded repetitions × positions
/// (single-character steps once counts are written out; an alternation counts all its branches).
/// </para>
/// </remarks>
internal sealed class ExpressionGrammar
{
    /// <summary>At most this many quantifiers.</summary>
    public const int MaxQuantifiers = 30;

    /// <summary>At most this many unbounded quantifiers (<c>* + {n,}</c>).</summary>
    public const int MaxUnbounded = 8;

    /// <summary>Unbounded quantifiers × positions may be at most this.</summary>
    public const int MaxComplexity = 120;

    private const int Unbounded = int.MaxValue;

    private readonly string _text;
    private int _i;
    private int _quantifiers;
    private int _unbounded;
    private DatePatternError? _error;

    private ExpressionGrammar(string text) => _text = text;

    /// <summary>The first reason <paramref name="text"/> is outside the subset or over the limits; null when it is fine.</summary>
    public static DatePatternError? Check(string text)
    {
        var parser = new ExpressionGrammar(text);
        var node = parser.ParseExpression();
        if (parser._error is null && parser._i < text.Length)
        {
            // Only a ")" without "(" ends the top level early.
            parser.Fail(DatePatternErrorKind.InvalidSyntax, parser._i + 1);
        }

        if (parser._error is not null)
        {
            return parser._error;
        }

        var cost = (long)parser._unbounded * node.Positions;
        return parser._quantifiers > MaxQuantifiers || parser._unbounded > MaxUnbounded || cost > MaxComplexity
            ? new DatePatternError(DatePatternErrorKind.TooComplex, 0)
            : null;
    }

    private static int Add(int a, int b) =>
        a == Unbounded || b == Unbounded ? Unbounded : (int)Math.Min((long)a + b, Unbounded - 1);

    private static int Times(int a, int count) =>
        a == 0 || count == 0 ? 0
        : a == Unbounded || count == Unbounded ? Unbounded
        : (int)Math.Min((long)a * count, Unbounded - 1);

    /// <summary>Records the first problem and ends parsing (every loop stops at the end of the text).</summary>
    private Node Fail(DatePatternErrorKind kind, int position, string? detail = null)
    {
        _error ??= new DatePatternError(kind, Math.Clamp(position, 0, _text.Length), detail);
        _i = _text.Length;
        return Node.Empty;
    }

    private char? Peek() => _i < _text.Length ? _text[_i] : null;

    private Node ParseExpression()
    {
        var node = ParseBranch();
        while (Peek() == '|')
        {
            _i++;
            var next = ParseBranch();
            node = new Node(
                Math.Min(node.Min, next.Min),
                Math.Max(node.Max, next.Max),
                node.Positions + next.Positions,
                node.ContainsVariable || next.ContainsVariable || node.Min != next.Min || node.Max != next.Max);
        }

        return node;
    }

    private Node ParseBranch()
    {
        var node = Node.Empty;
        while (Peek() is { } c && c != '|' && c != ')')
        {
            var piece = ParsePiece();
            node = new Node(
                Add(node.Min, piece.Min),
                Add(node.Max, piece.Max),
                node.Positions + piece.Positions,
                node.ContainsVariable || piece.ContainsVariable);
        }

        return node;
    }

    private Node ParsePiece()
    {
        var start = _i;
        var atom = ParseAtom();
        if (_error is not null || Peek() is not ('*' or '+' or '?' or '{'))
        {
            return atom;
        }

        var quantifierStart = _i;
        if (!TryParseQuantifier(out var min, out var max))
        {
            return Node.Empty;
        }

        if (atom.Max == 0)
        {
            // Repeating nothing (an anchor or an empty group) only adds work.
            return Fail(DatePatternErrorKind.UnsupportedConstruct, quantifierStart, _text[start..Math.Min(_i, _text.Length)]);
        }

        if (atom.ContainsVariable || atom.Min != atom.Max)
        {
            return Fail(DatePatternErrorKind.NestedQuantifier, quantifierStart, _text[quantifierStart.._i]);
        }

        _quantifiers++;
        if (max == Unbounded)
        {
            _unbounded++;
        }

        var repeat = max == Unbounded ? Math.Max(min, 1) : max;
        return new Node(Times(atom.Min, min), Times(atom.Max, max), atom.Positions * repeat, min != max);
    }

    private bool TryParseQuantifier(out int min, out int max)
    {
        var start = _i;
        var c = _text[_i++];
        (min, max) = c switch
        {
            '*' => (0, Unbounded),
            '+' => (1, Unbounded),
            '?' => (0, 1),
            _ => (-1, -1),
        };

        if (c == '{' && !TryParseCount(start, out min, out max))
        {
            return false;
        }

        if (Peek() == '?')
        {
            _i++; // lazy
        }

        if (Peek() is '*' or '+' or '?' or '{')
        {
            Fail(DatePatternErrorKind.InvalidSyntax, _i + 1);
            return false;
        }

        return true;
    }

    /// <summary>Reads <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c>; the <c>{</c> at <paramref name="open"/> is already read.</summary>
    private bool TryParseCount(int open, out int min, out int max)
    {
        min = max = 0;
        var first = ReadNumber();
        int? second = first;
        if (first is not null && Peek() == ',')
        {
            _i++;
            second = ReadNumber();
        }

        if (first is null || Peek() != '}')
        {
            // A "{" that is no repetition: write \{ for the character.
            Fail(DatePatternErrorKind.UnsupportedConstruct, open, "{");
            return false;
        }

        _i++;
        if (first > DateExpression.MaxRepetition || second > DateExpression.MaxRepetition)
        {
            Fail(DatePatternErrorKind.RepetitionTooLarge, open, _text[open.._i]);
            return false;
        }

        if (second < first)
        {
            Fail(DatePatternErrorKind.InvalidSyntax, open + 1);
            return false;
        }

        min = first.Value;
        max = second ?? Unbounded;
        return true;
    }

    private int? ReadNumber()
    {
        var start = _i;
        var value = 0;
        while (Peek() is { } c && char.IsAsciiDigit(c))
        {
            value = Math.Min((value * 10) + (c - '0'), 100_000);
            _i++;
        }

        return _i == start ? null : value;
    }

    private Node ParseAtom()
    {
        var start = _i;
        var c = _text[_i];
        switch (c)
        {
            case '(':
                return ParseGroup();
            case '[':
                ParseClass();
                return Node.Single;
            case '\\':
                ParseEscape();
                return Node.Single;
            case '.':
                _i++;
                return Node.Single;
            case '^' or '$':
                _i++;
                return Node.Empty;
            case '*' or '+' or '?':
                // A repetition with nothing before it.
                return Fail(DatePatternErrorKind.InvalidSyntax, start + 1);
            case '{' or '}' or ']':
                return Fail(DatePatternErrorKind.UnsupportedConstruct, start, c.ToString());
            default:
                if (char.IsControl(c))
                {
                    return Fail(DatePatternErrorKind.InvalidExpression, start);
                }

                _i++;
                return Node.Single;
        }
    }

    private Node ParseGroup()
    {
        var open = _i;
        _i++;
        if (Peek() == '?')
        {
            var next = _i + 1 < _text.Length ? _text[_i + 1] : '\0';
            var after = _i + 2 < _text.Length ? _text[_i + 2] : '\0';
            if (next == ':')
            {
                _i += 2;
            }
            else if (next == '<' && after is not ('=' or '!' or '\0'))
            {
                var nameStart = _i + 2;
                var close = _text.IndexOf('>', nameStart);
                if (close < 0)
                {
                    return Fail(DatePatternErrorKind.InvalidSyntax, _text.Length);
                }

                var name = _text[nameStart..close];
                if (!DateExpression.GroupNames.Contains(name, StringComparer.Ordinal))
                {
                    return Fail(DatePatternErrorKind.UnknownGroup, open, name);
                }

                _i = close + 1;
            }
            else
            {
                // Inline options, lookarounds, atomic groups, conditionals, comments, (?'name'…), balancing groups.
                return Fail(DatePatternErrorKind.UnsupportedConstruct, open, _text[open..Math.Min(_text.Length, _i + 2)]);
            }
        }

        var content = ParseExpression();
        if (_error is not null)
        {
            return Node.Empty;
        }

        if (Peek() != ')')
        {
            return Fail(DatePatternErrorKind.InvalidSyntax, _text.Length);
        }

        _i++;
        return content;
    }

    private void ParseClass()
    {
        var open = _i;
        _i++;
        if (Peek() == '^')
        {
            _i++;
        }

        var items = 0;
        while (_error is null)
        {
            switch (Peek())
            {
                case null:
                    Fail(DatePatternErrorKind.InvalidSyntax, _text.Length);
                    return;
                case ']' when items == 0:
                    // "[]" or "[^]": a "]" in a class must be written as \].
                    Fail(DatePatternErrorKind.UnsupportedConstruct, open, _text[open.._i] + "]");
                    return;
                case ']':
                    _i++;
                    return;
            }

            var itemStart = _i;
            var low = ParseClassChar();
            items++;
            if (_error is null && Peek() == '-' && _i + 1 < _text.Length && _text[_i + 1] != ']')
            {
                _i++;
                var high = ParseClassChar();
                if (_error is null && (low is null || high is null || high < low))
                {
                    Fail(DatePatternErrorKind.UnsupportedConstruct, itemStart, _text[itemStart.._i]);
                }
            }
        }
    }

    /// <summary>One character of a class; null for <c>\d \s \w</c>, which cannot start or end a range.</summary>
    private char? ParseClassChar()
    {
        var c = _text[_i];
        if (c == '[')
        {
            // No nested classes or subtraction ("-[…]"); write \[ for the character.
            Fail(DatePatternErrorKind.UnsupportedConstruct, _i, "[");
            return null;
        }

        if (c == '\\')
        {
            return ParseEscape();
        }

        if (char.IsControl(c))
        {
            Fail(DatePatternErrorKind.InvalidExpression, _i);
            return null;
        }

        _i++;
        return c;
    }

    /// <summary>Reads <c>\x</c>: the escaped character, or null for <c>\d \s \w</c>.</summary>
    private char? ParseEscape()
    {
        var start = _i;
        if (_i + 1 >= _text.Length)
        {
            Fail(DatePatternErrorKind.InvalidSyntax, _text.Length);
            return null;
        }

        var c = _text[_i + 1];
        _i += 2;
        if (c is 'd' or 's' or 'w')
        {
            return null;
        }

        if (char.IsLetterOrDigit(c) || c == '_' || char.IsControl(c))
        {
            // \b, \1, \p{…}, \k<…>, \n, A and the like.
            Fail(DatePatternErrorKind.UnsupportedConstruct, start, _text[start.._i]);
            return null;
        }

        return c;
    }

    /// <summary>Width and size of a part of the expression.</summary>
    /// <param name="Min">Shortest text it matches.</param>
    /// <param name="Max">Longest text it matches; <see cref="Unbounded"/> for no limit.</param>
    /// <param name="Positions">Single-character steps once counts are written out (all branches of an alternation).</param>
    /// <param name="ContainsVariable">True when it or anything inside it varies in width.</param>
    private sealed record Node(int Min, int Max, long Positions, bool ContainsVariable)
    {
        public static Node Empty { get; } = new(0, 0, 0, false);

        public static Node Single { get; } = new(1, 1, 1, false);
    }
}
