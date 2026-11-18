namespace Snapdate.Domain.Tests.FileNames;

/// <summary>Timing tests run on their own, never in parallel with other tests.</summary>
[CollectionDefinition(nameof(TimingTests), DisableParallelization = true)]
public sealed class TimingTests;
