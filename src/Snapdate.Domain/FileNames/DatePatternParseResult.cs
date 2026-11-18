namespace Snapdate.Domain.FileNames;

/// <summary>Outcome of <see cref="DatePattern.Parse"/>: a pattern or a list of errors.</summary>
public sealed class DatePatternParseResult
{
    private DatePatternParseResult(DatePattern? pattern, IReadOnlyList<DatePatternError> errors)
    {
        Pattern = pattern;
        Errors = errors;
    }

    public DatePattern? Pattern { get; }

    public IReadOnlyList<DatePatternError> Errors { get; }

    public bool IsValid => Pattern is not null;

    internal static DatePatternParseResult Succeeded(DatePattern pattern) => new(pattern, []);

    internal static DatePatternParseResult Failed(params DatePatternError[] errors) => new(null, errors);
}
