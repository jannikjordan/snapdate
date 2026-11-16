using Snapdate.Application.Ports;
using Snapdate.Application.Scanning;
using Snapdate.Application.Tests.Fakes;
using Snapdate.Domain.Photos;

namespace Snapdate.Application.Tests.Scanning;

public sealed class ScanSourceUseCaseTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "scan-src");

    private static async Task<List<ScannedPhoto>> Scan(FakeFileSystem fs, FakeMetadataReader reader, bool recursive = true)
    {
        var list = new List<ScannedPhoto>();
        await foreach (var photo in new ScanSourceUseCase(fs, reader).ScanAsync(Root, recursive, TestContext.Current.CancellationToken))
        {
            list.Add(photo);
        }

        return list;
    }

    [Fact]
    public async Task Only_supported_files_are_picked_up()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(Path.Combine(Root, "a.JPG"));
        fs.AddFile(Path.Combine(Root, "b.heic"));
        fs.AddFile(Path.Combine(Root, "c.mp4"));
        fs.AddFile(Path.Combine(Root, "notes.txt"));
        fs.AddFile(Path.Combine(Root, "desktop.ini"));

        var names = (await Scan(fs, new FakeMetadataReader())).Select(p => p.OriginalName + p.Extension).Order().ToList();

        Assert.Equal(["a.JPG", "b.heic", "c.mp4"], names);
    }

    [Fact]
    public async Task Metadata_date_is_preferred_over_file_date()
    {
        var fs = new FakeFileSystem();
        var path = Path.Combine(Root, "a.jpg");
        fs.AddFile(path, timestamp: new DateTime(2025, 1, 1));
        var reader = new FakeMetadataReader();
        reader.Metadata[path] = new PhotoMetadata(new DateTime(2024, 7, 14, 15, 32, 8), DateSource.ExifOriginal, "iPhone");

        var photo = Assert.Single(await Scan(fs, reader));

        Assert.Equal(new DateTime(2024, 7, 14, 15, 32, 8), photo.Date.Value);
        Assert.Equal(DateSource.ExifOriginal, photo.Date.Source);
        Assert.Equal("iPhone", photo.CameraModel);
    }

    [Fact]
    public async Task Files_without_metadata_fall_back_to_the_file_date()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(Path.Combine(Root, "a.png"), timestamp: new DateTime(2023, 3, 3, 3, 3, 3));

        var photo = Assert.Single(await Scan(fs, new FakeMetadataReader()));

        Assert.Equal(DateSource.FileTimestamp, photo.Date.Source);
        Assert.Equal(new DateTime(2023, 3, 3, 3, 3, 3), photo.Date.Value);
        Assert.False(photo.Date.IsFromMetadata);
    }

    [Fact]
    public async Task Sub_folders_are_only_scanned_when_requested()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(Path.Combine(Root, "a.jpg"));
        fs.AddFile(Path.Combine(Root, "sub", "b.jpg"));

        Assert.Single(await Scan(fs, new FakeMetadataReader(), recursive: false));
        Assert.Equal(2, (await Scan(fs, new FakeMetadataReader(), recursive: true)).Count);
    }
}
