namespace Snapdate.Application.Execution;

/// <summary>Summary of a finished (or cancelled) run.</summary>
public sealed record ExecutionResult(IReadOnlyList<ItemOutcome> Outcomes, string? LogFilePath)
{
    public int DoneCount => Outcomes.Count(o => o.Status == ItemStatus.Done);

    public int UnchangedCount => Outcomes.Count(o => o.Status == ItemStatus.Unchanged);

    public int FailedCount => Outcomes.Count(o => o.Status == ItemStatus.Failed);

    public bool WasCancelled => Outcomes.Any(o => o.Status == ItemStatus.Cancelled);
}
