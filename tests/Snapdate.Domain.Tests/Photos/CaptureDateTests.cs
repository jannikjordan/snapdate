using Snapdate.Domain.Photos;

namespace Snapdate.Domain.Tests.Photos;

public sealed class CaptureDateTests
{
    [Theory]
    [InlineData(DateSource.ExifOriginal, true, false)]
    [InlineData(DateSource.QuickTime, true, false)]
    [InlineData(DateSource.PngText, true, false)]
    [InlineData(DateSource.FileName, false, true)]
    [InlineData(DateSource.FileTimestamp, false, false)]
    public void Flags_tell_where_the_date_came_from(DateSource source, bool fromMetadata, bool fromFileName)
    {
        var date = new CaptureDate(new DateTime(2024, 7, 14), source);

        Assert.Equal(fromMetadata, date.IsFromMetadata);
        Assert.Equal(fromFileName, date.IsFromFileName);
    }
}
