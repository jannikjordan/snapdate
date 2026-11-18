using Snapdate.Application.Execution;
using Snapdate.Application.Planning;
using Snapdate.Application.Scanning;
using Snapdate.Application.Tests.Fakes;
using Snapdate.Domain.Photos;
using Snapdate.Domain.Planning;

namespace Snapdate.Application.Tests.Execution;

public sealed class ExecutePlanUseCaseTests
{
    private static readonly string Source = Path.Combine(Path.GetTempPath(), "exec-src");
    private static readonly string Target = Path.Combine(Path.GetTempPath(), "exec-dst");

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static (FakeFileSystem Fs, RenamePlan Plan) Setup(TransferMode mode, params (string File, string Target)[] items)
    {
        var fs = new FakeFileSystem();
        var planItems = items.Select(i =>
        {
            var path = Path.Combine(Source, i.File);
            fs.AddFile(path);
            var photo = new ScannedPhoto(path, Path.GetFileNameWithoutExtension(i.File), Path.GetExtension(i.File),
                new CaptureDate(new DateTime(2024, 1, 1), DateSource.ExifOriginal), null, 100);
            return new RenameItem(photo, i.Target, false);
        }).ToList();
        return (fs, new RenamePlan(Source, Target, mode, planItems));
    }

    private static Task<ExecutionResult> Run(FakeFileSystem fs, RenamePlan plan, CancellationToken? ct = null) =>
        new ExecutePlanUseCase(fs, new FixedClock()).ExecuteAsync(plan, null, ct ?? TestContext.Current.CancellationToken);

    [Fact]
    public async Task Copy_keeps_originals_and_creates_targets()
    {
        var (fs, plan) = Setup(TransferMode.Copy, ("a.jpg", "2024/a.jpg"), ("b.jpg", "2024/b.jpg"));

        var result = await Run(fs, plan);

        Assert.Equal(2, result.DoneCount);
        Assert.True(fs.FileExists(Path.Combine(Source, "a.jpg")));
        Assert.True(fs.FileExists(Path.Combine(Target, "2024", "a.jpg")));
        Assert.All(fs.Operations, op => Assert.Equal("copy", op.Op));
    }

    [Fact]
    public async Task Move_removes_originals()
    {
        var (fs, plan) = Setup(TransferMode.Move, ("a.jpg", "x.jpg"));

        await Run(fs, plan);

        Assert.False(fs.FileExists(Path.Combine(Source, "a.jpg")));
        Assert.True(fs.FileExists(Path.Combine(Target, "x.jpg")));
    }

    [Fact]
    public async Task File_appearing_after_planning_is_not_overwritten()
    {
        var (fs, plan) = Setup(TransferMode.Copy, ("a.jpg", "x.jpg"));
        var intruder = Path.Combine(Target, "x.jpg");
        fs.BeforeTransfer = target =>
        {
            if (!fs.FileExists(intruder))
            {
                fs.AddFile(intruder, length: 999);
            }
        };

        var outcome = Assert.Single((await Run(fs, plan)).Outcomes);

        Assert.Equal(ItemStatus.Done, outcome.Status);
        Assert.Equal("x (2).jpg", outcome.FinalRelativePath);
        Assert.Equal(999, fs.GetLength(intruder));
    }

    [Fact]
    public async Task A_failing_file_does_not_stop_the_others()
    {
        var (fs, plan) = Setup(TransferMode.Copy, ("bad.jpg", "1.jpg"), ("good.jpg", "2.jpg"));
        fs.FailingSources.Add(Path.Combine(Source, "bad.jpg"));

        var result = await Run(fs, plan);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.DoneCount);
        Assert.Equal("Simulated disk error.", result.Outcomes[0].Error);
    }

    [Fact]
    public async Task A_log_file_is_written_into_the_target_folder()
    {
        var (fs, plan) = Setup(TransferMode.Copy, ("a.jpg", "x.jpg"), ("IMG-20160706-WA0002.jpg", "y.jpg"));
        var fromName = plan.Items[1] with
        {
            Photo = plan.Items[1].Photo with { Date = new CaptureDate(new DateTime(2016, 7, 6), DateSource.FileName) },
        };
        plan = plan with { Items = [plan.Items[0], fromName] };

        var result = await Run(fs, plan);

        Assert.Equal(Path.Combine(Target, "snapdate-log-20261116-120000.csv"), result.LogFilePath);
        var csv = fs.TextFiles[result.LogFilePath!];
        Assert.StartsWith("status,mode,source,target,date_taken,date_source,error", csv, StringComparison.Ordinal);
        Assert.Contains("Done,Copy,", csv, StringComparison.Ordinal);
        Assert.Contains(",2024-01-01 00:00:00,ExifOriginal,", csv, StringComparison.Ordinal);
        Assert.Contains(",2016-07-06 00:00:00,FileName,", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_marks_remaining_items_as_cancelled()
    {
        var (fs, plan) = Setup(TransferMode.Copy, ("a.jpg", "1.jpg"), ("b.jpg", "2.jpg"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().WaitAsync(TestContext.Current.CancellationToken);

        var result = await Run(fs, plan, cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(0, result.DoneCount);
        Assert.Empty(fs.Operations);
    }

    [Fact]
    public async Task Renaming_in_place_to_the_same_name_is_left_alone()
    {
        var fs = new FakeFileSystem();
        var path = Path.Combine(Source, "x.jpg");
        fs.AddFile(path);
        var photo = new ScannedPhoto(path, "x", ".jpg", new CaptureDate(new DateTime(2024, 1, 1), DateSource.ExifOriginal), null, 1);
        var plan = new RenamePlan(Source, Source, TransferMode.Move, [new RenameItem(photo, "x.jpg", false)]);

        var outcome = Assert.Single((await Run(fs, plan)).Outcomes);

        Assert.Equal(ItemStatus.Unchanged, outcome.Status);
        Assert.Empty(fs.Operations);
    }
}
