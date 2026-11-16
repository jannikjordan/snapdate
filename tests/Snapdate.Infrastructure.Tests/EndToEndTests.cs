using System.Globalization;
using Snapdate.Application.Execution;
using Snapdate.Application.Planning;
using Snapdate.Application.Scanning;
using Snapdate.Domain.Naming;
using Snapdate.Domain.Planning;
using Snapdate.Infrastructure.FileSystem;
using Snapdate.Infrastructure.Metadata;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.Infrastructure.Tests;

/// <summary>Scan → plan → copy on real files, the way the app runs it.</summary>
public sealed class EndToEndTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Photos_are_copied_with_date_names_and_originals_stay()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = _temp.Combine("camera");
        var target = _temp.Combine("sorted");
        _temp.Write("camera/IMG_0001.JPG", TestImages.JpegWithExif(new DateTime(2024, 7, 14, 15, 32, 8), "iPhone 15 Pro"));
        _temp.Write("camera/IMG_0002.JPG", TestImages.JpegWithExif(new DateTime(2024, 7, 14, 15, 32, 8), "iPhone 15 Pro"));
        _temp.Write("camera/sub/Screenshot.png", TestImages.PngWithCreationTime(null), new DateTime(2023, 1, 2, 3, 4, 5));
        _temp.Write("camera/notes.txt", [1, 2, 3]);

        var fs = new LocalFileSystem();
        var photos = new List<ScannedPhoto>();
        await foreach (var p in new ScanSourceUseCase(fs, new MetadataExtractorReader(TimeProvider.System)).ScanAsync(source, true, ct))
        {
            photos.Add(p);
        }

        var plan = new BuildPlanUseCase(fs).Build(
            photos, NamePattern.Parse("{YYYY}-{MM}-{DD} {HH}{mm}").Pattern!, CultureInfo.GetCultureInfo("en-US"), source, target, TransferMode.Copy);
        var result = await new ExecutePlanUseCase(fs, TimeProvider.System).ExecuteAsync(plan, null, ct);

        Assert.Equal(3, result.DoneCount);
        var written = Directory.GetFiles(target).Select(Path.GetFileName).Order().ToList();
        Assert.Contains("2023-01-02 0304.png", written);
        Assert.Contains("2024-07-14 1532.jpg", written);
        Assert.Contains("2024-07-14 1532 (2).jpg", written);
        Assert.Contains(written, n => n!.StartsWith("snapdate-log-", StringComparison.Ordinal));
        Assert.Equal(4, Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length);
    }
}
