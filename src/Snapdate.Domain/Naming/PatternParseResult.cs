namespace Snapdate.Domain.Naming;

/// <summary>Outcome of <see cref="NamePattern.Parse"/>: a pattern or a list of errors.</summary>
public sealed class PatternParseResult
{
    private PatternParseResult(NamePattern? pattern, IReadOnlyList<PatternError> errors)
    {
        Pattern = pattern;
        Errors = errors;
    }

    public NamePattern? Pattern { get; }

    public IReadOnlyList<PatternError> Errors { get; }

    public bool IsValid => Pattern is not null;

    internal static PatternParseResult Succeeded(NamePattern pattern) => new(pattern, []);

    internal static PatternParseResult Failed(params PatternError[] errors) => new(null, errors);
}
