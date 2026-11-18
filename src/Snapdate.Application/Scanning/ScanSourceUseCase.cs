using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Snapdate.Application.Ports;
using Snapdate.Domain.FileNames;
using Snapdate.Domain.Photos;

namespace Snapdate.Application.Scanning;

/// <summary>
/// Finds supported files in a folder and resolves each capture date from metadata, a date in the
/// file name (<paramref name="dateRules"/>) and the file timestamp, as decided by
/// <see cref="CaptureDateResolver"/>. Results stream in as they are read.
/// </summary>
/// <param name="fileSystem">File system access.</param>
/// <param name="metadataReader">Reads embedded metadata.</param>
/// <param name="dateRules">Rules that read dates from file names; <see cref="DateRuleSet.Empty"/> when null.</param>
/// <param name="timeProvider">Gives "now" for the plausibility check of file-name dates; system time when null.</param>
public sealed class ScanSourceUseCase(
    IFileSystem fileSystem,
    IMetadataReader metadataReader,
    DateRuleSet? dateRules = null,
    TimeProvider? timeProvider = null)
{
    private readonly DateRuleSet _dateRules = dateRules ?? DateRuleSet.Empty;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Number of files read in parallel; metadata reads are I/O bound and small.</summary>
    public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 2, 8);

    public async IAsyncEnumerable<ScannedPhoto> ScanAsync(
        string root,
        bool recursive,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var localNow = _timeProvider.GetLocalNow().DateTime;

        var channel = Channel.CreateBounded<ScannedPhoto>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
        });

        var producer = Task.Run(
            async () =>
            {
                try
                {
                    var files = fileSystem.EnumerateFiles(root, recursive).Where(SupportedFormats.IsSupported);
                    await Parallel.ForEachAsync(
                        files,
                        new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
                        async (path, ct) =>
                        {
                            var photo = Read(path, localNow);
                            if (photo is not null)
                            {
                                await channel.Writer.WriteAsync(photo, ct).ConfigureAwait(false);
                            }
                        }).ConfigureAwait(false);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            },
            cancellationToken);

        await foreach (var photo in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return photo;
        }

        await producer.ConfigureAwait(false);
    }

    private ScannedPhoto? Read(string path, DateTime localNow)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var metadata = metadataReader.Read(path);
            CaptureDate? metadataDate = metadata is { Taken: { } taken, Source: { } source }
                ? new CaptureDate(taken, source)
                : null;
            var resolved = CaptureDateResolver.Resolve(
                metadataDate,
                metadataFromCamera: metadata.CameraModel is not null,
                _dateRules.Match(name, localNow),
                () => fileSystem.GetEarliestTimestamp(path));

            return new ScannedPhoto(
                path,
                name,
                Path.GetExtension(path),
                resolved.Date,
                metadata.CameraModel,
                fileSystem.GetLength(path),
                resolved.RuleName,
                resolved.IgnoredMetadataDate,
                resolved.IgnoredFileNameDate);
        }
        catch (IOException)
        {
            return null; // vanished or locked while scanning: simply not part of the plan
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
