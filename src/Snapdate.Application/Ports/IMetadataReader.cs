using Snapdate.Domain.Photos;

namespace Snapdate.Application.Ports;

/// <summary>What could be read from a file's embedded metadata. All fields are optional.</summary>
/// <param name="Taken">Capture date as local wall-clock time, if present.</param>
/// <param name="Source">Which metadata field <paramref name="Taken"/> came from.</param>
/// <param name="CameraModel">Camera model, e.g. "iPhone 15 Pro".</param>
public sealed record PhotoMetadata(DateTime? Taken, DateSource? Source, string? CameraModel)
{
    public static PhotoMetadata None { get; } = new(null, null, null);
}

/// <summary>Reads embedded metadata. Must never throw for malformed files.</summary>
public interface IMetadataReader
{
    PhotoMetadata Read(string path);
}
