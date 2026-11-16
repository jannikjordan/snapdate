using Snapdate.Application.Ports;

namespace Snapdate.Application.Tests.Fakes;

internal sealed class FakeMetadataReader : IMetadataReader
{
    public Dictionary<string, PhotoMetadata> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PhotoMetadata Read(string path) =>
        Metadata.TryGetValue(Path.GetFullPath(path), out var metadata) ? metadata : PhotoMetadata.None;
}
