namespace Snapdate.Domain.FileNames;

/// <summary>Outcome of <see cref="DateExpression.Parse"/>: an expression or a list of errors.</summary>
public sealed class DateExpressionParseResult
{
    private DateExpressionParseResult(DateExpression? expression, IReadOnlyList<DatePatternError> errors)
    {
        Expression = expression;
        Errors = errors;
    }

    public DateExpression? Expression { get; }

    public IReadOnlyList<DatePatternError> Errors { get; }

    public bool IsValid => Expression is not null;

    internal static DateExpressionParseResult Succeeded(DateExpression expression) => new(expression, []);

    internal static DateExpressionParseResult Failed(params DatePatternError[] errors) => new(null, errors);
}
