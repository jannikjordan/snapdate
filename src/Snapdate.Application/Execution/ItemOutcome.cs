using Snapdate.Application.Planning;

namespace Snapdate.Application.Execution;

public enum ItemStatus
{
    Done,
    Unchanged,
    Failed,
    Cancelled,
}

/// <summary>What happened to one planned item.</summary>
/// <param name="Item">The planned item.</param>
/// <param name="Status">Result.</param>
/// <param name="FinalRelativePath">Where it ended up (may differ from the plan if a file appeared meanwhile).</param>
/// <param name="Error">Plain error message for failed items.</param>
public sealed record ItemOutcome(RenameItem Item, ItemStatus Status, string? FinalRelativePath, string? Error);
