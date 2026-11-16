using System.Globalization;
using System.Text;
using Snapdate.Application.Planning;
using Snapdate.Application.Ports;
using Snapdate.Domain.Paths;
using Snapdate.Domain.Planning;

namespace Snapdate.Application.Execution;

/// <summary>
/// Copies or moves every planned file. Never overwrites: if a target appeared after planning, the
/// next free " (n)" name is used. Writes a CSV log into the target folder.
/// </summary>
public sealed class ExecutePlanUseCase(IFileSystem fileSystem, TimeProvider clock)
{
    public async Task<ExecutionResult> ExecuteAsync(
        RenamePlan plan,
        IProgress<ExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.TargetRoot))
        {
            throw new InvalidOperationException("A preview plan without target folder cannot be executed.");
        }

        var outcomes = new List<ItemOutcome>(plan.Items.Count);
        var resolver = new UniqueNameResolver(relative =>
            fileSystem.FileExists(PathGuard.CombineInside(plan.TargetRoot, relative)));

        for (var i = 0; i < plan.Items.Count; i++)
        {
            var item = plan.Items[i];
            if (cancellationToken.IsCancellationRequested)
            {
                outcomes.Add(new ItemOutcome(item, ItemStatus.Cancelled, null, null));
                continue;
            }

            progress?.Report(new ExecutionProgress(i, plan.Items.Count, item.Photo.OriginalName + item.Photo.Extension));
            outcomes.Add(await RunItemAsync(plan, item, resolver, cancellationToken).ConfigureAwait(false));
        }

        progress?.Report(new ExecutionProgress(plan.Items.Count, plan.Items.Count, string.Empty));

        string? logPath = null;
        if (outcomes.Any(o => o.Status is ItemStatus.Done or ItemStatus.Failed))
        {
            logPath = await WriteLogAsync(plan, outcomes).ConfigureAwait(false);
        }

        return new ExecutionResult(outcomes, logPath);
    }

    private async Task<ItemOutcome> RunItemAsync(
        RenamePlan plan,
        RenameItem item,
        UniqueNameResolver resolver,
        CancellationToken cancellationToken)
    {
        var relative = item.TargetRelativePath;
        try
        {
            var target = PathGuard.CombineInside(plan.TargetRoot, relative);
            if (string.Equals(Path.GetFullPath(item.Photo.SourcePath), target, StringComparison.OrdinalIgnoreCase))
            {
                return new ItemOutcome(item, ItemStatus.Unchanged, relative, null);
            }

            for (var attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    fileSystem.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (plan.Mode == TransferMode.Move)
                    {
                        await fileSystem.MoveAsync(item.Photo.SourcePath, target, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await fileSystem.CopyAsync(item.Photo.SourcePath, target, cancellationToken).ConfigureAwait(false);
                    }

                    return new ItemOutcome(item, ItemStatus.Done, relative, null);
                }
                catch (TargetExistsException)
                {
                    // Someone created the file after planning: pick the next free name, never overwrite.
                    var stem = relative[..^Path.GetExtension(relative).Length];
                    relative = resolver.Reserve(StripCounter(stem), Path.GetExtension(relative), out _);
                    target = PathGuard.CombineInside(plan.TargetRoot, relative);
                }
            }

            return new ItemOutcome(item, ItemStatus.Failed, null, "No free file name found.");
        }
        catch (OperationCanceledException)
        {
            return new ItemOutcome(item, ItemStatus.Cancelled, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ItemOutcome(item, ItemStatus.Failed, null, ex.Message);
        }
    }

    // "name (3)" -> "name" so retries count up from the base name.
    private static string StripCounter(string stem)
    {
        var open = stem.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && stem.EndsWith(')') && int.TryParse(stem[(open + 2)..^1], out _)
            ? stem[..open]
            : stem;
    }

    private async Task<string?> WriteLogAsync(RenamePlan plan, List<ItemOutcome> outcomes)
    {
        var now = clock.GetLocalNow();
        var name = $"snapdate-log-{now:yyyyMMdd-HHmmss}.csv";
        var path = PathGuard.CombineInside(plan.TargetRoot, name);

        var csv = new StringBuilder();
        csv.AppendLine("status,mode,source,target,date_taken,date_source,error");
        foreach (var o in outcomes)
        {
            csv.Append(Csv(o.Status.ToString())).Append(',')
               .Append(Csv(plan.Mode.ToString())).Append(',')
               .Append(Csv(o.Item.Photo.SourcePath)).Append(',')
               .Append(Csv(o.FinalRelativePath ?? string.Empty)).Append(',')
               .Append(Csv(o.Item.Photo.Date.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
               .Append(Csv(o.Item.Photo.Date.Source.ToString())).Append(',')
               .AppendLine(Csv(o.Error ?? string.Empty));
        }

        try
        {
            await fileSystem.WriteAllTextAsync(path, csv.ToString(), CancellationToken.None).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // the log is a convenience; never fail the run because of it
        }
    }

    // RFC 4180 quoting; also defuses spreadsheet formula injection (=, +, -, @).
    private static string Csv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
