namespace Snapdate.Application.Execution;

/// <summary>Progress report while copying/moving.</summary>
public readonly record struct ExecutionProgress(int Completed, int Total, string CurrentFileName);
