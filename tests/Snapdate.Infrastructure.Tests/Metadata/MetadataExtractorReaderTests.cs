using Snapdate.Domain.Photos;
using Snapdate.Infrastructure.Metadata;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.Infrastructure.Tests.Metadata;

public sealed class MetadataExtractorReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly MetadataExtractorReader _reader = new(TimeProvider.System);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Reads_exif_date_taken_and_camera_model_from_jpeg()
    {
        var path = _temp.Write("a.jpg", TestImages.JpegWithExif(new DateTime(2024, 7, 14, 15, 32, 8), "iPhone 15 Pro"));

        var metadata = _reader.Read(path);

        Assert.Equal(new DateTime(2024, 7, 14, 15, 32, 8), metadata.Taken);
        Assert.Equal(DateSource.ExifOriginal, metadata.Source);
        Assert.Equal("iPhone 15 Pro", metadata.CameraModel);
    }

    [Fact]
    public void Reads_camera_model_even_without_a_date()
    {
        var metadata = _reader.Read(_temp.Write("a.jpg", TestImages.JpegWithExif(null, "ILCE-7M4")));
        Assert.Null(metadata.Taken);
        Assert.Equal("ILCE-7M4", metadata.CameraModel);
    }

    [Fact]
    public void Reads_png_creation_time()
    {
        var path = _temp.Write("a.png", TestImages.PngWithCreationTime("2023-05-06T07:08:09+02:00"));

        var metadata = _reader.Read(path);

        Assert.Equal(new DateTime(2023, 5, 6, 7, 8, 9), metadata.Taken);
        Assert.Equal(DateSource.PngText, metadata.Source);
    }

    [Fact]
    public void Png_without_metadata_has_no_date()
    {
        Assert.Null(_reader.Read(_temp.Write("a.png", TestImages.PngWithCreationTime(null))).Taken);
    }

    [Fact]
    public void Implausible_future_dates_are_ignored()
    {
        var metadata = _reader.Read(_temp.Write("a.jpg", TestImages.JpegWithExif(new DateTime(2999, 1, 1), null)));
        Assert.Null(metadata.Taken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4096)]
    public void Garbage_or_truncated_files_never_throw(int size)
    {
        var bytes = new byte[size];
        new Random(42).NextBytes(bytes);
        var metadata = _reader.Read(_temp.Write("broken.jpg", bytes));
        Assert.Null(metadata.Taken);
    }

    [Fact]
    public void Truncated_exif_segment_never_throws()
    {
        var full = TestImages.JpegWithExif(new DateTime(2024, 1, 1), "Cam");
        var metadata = _reader.Read(_temp.Write("cut.jpg", full[..(full.Length / 2)]));
        Assert.NotNull(metadata);
    }

    [Fact]
    public void Missing_file_returns_no_metadata()
    {
        Assert.Same(Snapdate.Application.Ports.PhotoMetadata.None, _reader.Read(_temp.Combine("missing.jpg")));
    }
}
