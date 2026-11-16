using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Snapdate.Application.Ports;
using Snapdate.Domain.Photos;

namespace Snapdate.Application.Scanning;

/// <summary>
/// Finds supported files in a folder and resolves each capture date
/// (metadata first, file timestamp as fallback). Results stream in as they are read.
/// </summary>
public sealed class ScanSourceUseCase(IFileSystem fileSystem, IMetadataReader metadataReader)
{
    /// <summary>Number of files read in parallel; metadata reads are I/O bound and small.</summary>
    public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 2, 8);

    public async IAsyncEnumerable<ScannedPhoto> ScanAsync(
        string root,
        bool recursive,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

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
                            var photo = Read(path);
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

    private ScannedPhoto? Read(string path)
    {
        try
        {
            var metadata = metadataReader.Read(path);
            var date = metadata is { Taken: { } taken, Source: { } source }
                ? new CaptureDate(taken, source)
                : new CaptureDate(fileSystem.GetEarliestTimestamp(path), DateSource.FileTimestamp);

            return new ScannedPhoto(
                path,
                Path.GetFileNameWithoutExtension(path),
                Path.GetExtension(path),
                date,
                metadata.CameraModel,
                fileSystem.GetLength(path));
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
