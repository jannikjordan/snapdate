namespace Snapdate.Domain.Naming;

/// <summary>One piece of a <see cref="NamePattern"/>: literal text or a placeholder.</summary>
public abstract record PatternSegment
{
    private PatternSegment()
    {
    }

    /// <summary>Text kept exactly as typed. A <c>/</c> starts a sub-folder.</summary>
    public sealed record Literal(string Text) : PatternSegment;

    /// <summary>A placeholder such as <c>{YYYY}</c>.</summary>
    public sealed record Token(TokenKind Kind) : PatternSegment;
}
