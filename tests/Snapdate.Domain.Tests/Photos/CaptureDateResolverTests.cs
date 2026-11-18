using Snapdate.Domain.FileNames;
using Snapdate.Domain.Photos;

namespace Snapdate.Domain.Tests.Photos;

public sealed class CaptureDateResolverTests
{
    private const string RuleName = "WhatsApp (Android)";

    private static readonly DateTime DownloadTime = new(2026, 11, 18, 8, 6, 0);

    private static DateRuleMatch Name(int year, int month, int day, TimeOnly? time = null) =>
        new(new FileNameDate(new DateOnly(year, month, day), time), "whatsapp-android", RuleName, 0);

    private static CaptureDate Exif(DateTime value, DateSource source = DateSource.ExifOriginal) => new(value, source);

    /// <summary>A file timestamp that counts how often it was read.</summary>
    private sealed class Timestamp(DateTime value)
    {
        public int Reads { get; private set; }

        public DateTime Read()
        {
            Reads++;
            return value;
        }
    }

    private static ResolvedDate Resolve(CaptureDate? metadata, bool fromCamera, DateRuleMatch? name, Timestamp? timestamp = null) =>
        CaptureDateResolver.Resolve(metadata, fromCamera, name, (timestamp ?? new Timestamp(DownloadTime)).Read);

    // --- neither metadata nor name ---------------------------------------------------------------

    [Fact]
    public void Without_metadata_and_name_the_file_timestamp_is_used()
    {
        var resolved = Resolve(null, fromCamera: false, name: null);

        Assert.Equal(new CaptureDate(DownloadTime, DateSource.FileTimestamp), resolved.Date);
        Assert.Null(resolved.RuleName);
        Assert.Null(resolved.IgnoredMetadataDate);
        Assert.Null(resolved.IgnoredFileNameDate);
        Assert.False(resolved.DatesDiffer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Without_name_metadata_is_used_unchanged(bool fromCamera)
    {
        var timestamp = new Timestamp(DownloadTime);
        var metadata = Exif(new DateTime(2026, 8, 12, 20, 44, 14));

        var resolved = Resolve(metadata, fromCamera, name: null, timestamp);

        Assert.Equal(metadata, resolved.Date);
        Assert.False(resolved.DatesDiffer);
        Assert.Equal(0, timestamp.Reads);
    }

    // --- name only -------------------------------------------------------------------------------

    [Fact]
    public void Name_with_time_is_used_and_the_file_timestamp_is_not_read()
    {
        var timestamp = new Timestamp(DownloadTime);

        var resolved = Resolve(null, fromCamera: false, Name(2024, 7, 14, new TimeOnly(15, 32, 8)), timestamp);

        Assert.Equal(new CaptureDate(new DateTime(2024, 7, 14, 15, 32, 8), DateSource.FileName), resolved.Date);
        Assert.Equal(RuleName, resolved.RuleName);
        Assert.False(resolved.DatesDiffer);
        Assert.Equal(0, timestamp.Reads);
    }

    [Fact]
    public void Date_only_name_takes_the_time_of_a_same_day_file_timestamp()
    {
        var resolved = Resolve(null, fromCamera: false, Name(2026, 11, 18));

        Assert.Equal(new CaptureDate(DownloadTime, DateSource.FileName), resolved.Date);
        Assert.False(resolved.DatesDiffer);
    }

    [Fact]
    public void Date_only_name_uses_midnight_when_the_file_timestamp_is_on_another_day()
    {
        var resolved = Resolve(null, fromCamera: false, Name(2026, 11, 17));

        Assert.Equal(new CaptureDate(new DateTime(2026, 11, 17), DateSource.FileName), resolved.Date);
    }

    [Fact]
    public void Date_only_name_uses_midnight_when_the_file_timestamp_is_a_minute_into_the_next_day()
    {
        var resolved = Resolve(null, fromCamera: false, Name(2026, 11, 17), new Timestamp(new DateTime(2026, 11, 18, 0, 0, 0)));

        Assert.Equal(new DateTime(2026, 11, 17), resolved.Date.Value);
    }

    [Fact]
    public void Date_only_name_keeps_a_file_timestamp_at_one_minute_to_midnight()
    {
        var resolved = Resolve(null, fromCamera: false, Name(2026, 11, 17), new Timestamp(new DateTime(2026, 11, 17, 23, 59, 59)));

        Assert.Equal(new DateTime(2026, 11, 17, 23, 59, 59), resolved.Date.Value);
    }

    // --- app-written metadata (no camera model) + name ------------------------------------------

    [Fact]
    public void Same_day_app_metadata_is_kept_unchanged()
    {
        var timestamp = new Timestamp(DownloadTime);
        var metadata = Exif(new DateTime(2026, 8, 12, 20, 44, 14));

        var resolved = Resolve(metadata, fromCamera: false, Name(2026, 8, 12), timestamp);

        Assert.Equal(metadata, resolved.Date);
        Assert.Equal(RuleName, resolved.RuleName);
        Assert.False(resolved.DatesDiffer);
        Assert.Null(resolved.IgnoredMetadataDate);
        Assert.Equal(0, timestamp.Reads);
    }

    [Fact]
    public void Same_day_metadata_time_wins_over_the_time_in_the_name()
    {
        var metadata = Exif(new DateTime(2024, 7, 14, 15, 32, 9), DateSource.Xmp);

        var resolved = Resolve(metadata, fromCamera: false, Name(2024, 7, 14, new TimeOnly(9, 0, 0)));

        Assert.Equal(metadata, resolved.Date);
        Assert.False(resolved.DatesDiffer);
    }

    [Fact]
    public void Different_day_app_metadata_is_ignored_in_favour_of_the_name()
    {
        var metadata = Exif(new DateTime(2026, 6, 23, 8, 34, 32));

        var resolved = Resolve(metadata, fromCamera: false, Name(2016, 7, 6));

        Assert.Equal(new CaptureDate(new DateTime(2016, 7, 6), DateSource.FileName), resolved.Date);
        Assert.Equal(RuleName, resolved.RuleName);
        Assert.Equal(new DateTime(2026, 6, 23, 8, 34, 32), resolved.IgnoredMetadataDate);
        Assert.Null(resolved.IgnoredFileNameDate);
        Assert.True(resolved.DatesDiffer);
    }

    [Fact]
    public void Different_day_name_with_time_keeps_its_time()
    {
        var resolved = Resolve(Exif(new DateTime(2026, 6, 23, 8, 34, 32)), fromCamera: false, Name(2024, 7, 14, new TimeOnly(15, 32, 8)));

        Assert.Equal(new DateTime(2024, 7, 14, 15, 32, 8), resolved.Date.Value);
        Assert.Equal(DateSource.FileName, resolved.Date.Source);
    }

    [Fact]
    public void Different_day_date_only_name_takes_the_time_of_a_same_day_file_timestamp()
    {
        var resolved = Resolve(
            Exif(new DateTime(2026, 11, 18, 8, 0, 0), DateSource.ExifDateTime),
            fromCamera: false,
            Name(2026, 8, 12),
            new Timestamp(new DateTime(2026, 8, 12, 19, 1, 2)));

        Assert.Equal(new CaptureDate(new DateTime(2026, 8, 12, 19, 1, 2), DateSource.FileName), resolved.Date);
        Assert.True(resolved.DatesDiffer);
    }

    [Fact]
    public void Metadata_one_minute_before_midnight_is_another_day()
    {
        var resolved = Resolve(Exif(new DateTime(2026, 8, 11, 23, 59, 0)), fromCamera: false, Name(2026, 8, 12));

        Assert.Equal(new CaptureDate(new DateTime(2026, 8, 12), DateSource.FileName), resolved.Date);
        Assert.Equal(new DateTime(2026, 8, 11, 23, 59, 0), resolved.IgnoredMetadataDate);
        Assert.True(resolved.DatesDiffer);
    }

    [Fact]
    public void Metadata_at_midnight_is_the_same_day()
    {
        var metadata = Exif(new DateTime(2026, 8, 12, 0, 0, 0));

        var resolved = Resolve(metadata, fromCamera: false, Name(2026, 8, 12));

        Assert.Equal(metadata, resolved.Date);
        Assert.False(resolved.DatesDiffer);
    }

    [Theory]
    [InlineData(DateSource.QuickTime)]
    [InlineData(DateSource.Xmp)]
    [InlineData(DateSource.PngText)]
    [InlineData(DateSource.ExifDigitized)]
    public void Any_metadata_without_camera_model_counts_as_app_written(DateSource source)
    {
        var resolved = Resolve(Exif(new DateTime(2026, 6, 23, 8, 34, 32), source), fromCamera: false, Name(2016, 7, 6));

        Assert.Equal(DateSource.FileName, resolved.Date.Source);
        Assert.True(resolved.DatesDiffer);
    }

    // --- camera metadata + name ------------------------------------------------------------------

    [Theory]
    [InlineData(2016, 7, 6)] // camera date later than the name
    [InlineData(2027, 1, 2)] // camera date earlier than the name
    public void Camera_metadata_always_wins_and_flags_a_different_name_date(int year, int month, int day)
    {
        var timestamp = new Timestamp(DownloadTime);
        var metadata = Exif(new DateTime(2026, 6, 23, 8, 34, 32));
        var name = Name(year, month, day);

        var resolved = Resolve(metadata, fromCamera: true, name, timestamp);

        Assert.Equal(metadata, resolved.Date);
        Assert.True(resolved.DatesDiffer);
        Assert.Null(resolved.IgnoredMetadataDate);
        Assert.Equal(name.Date, resolved.IgnoredFileNameDate);
        Assert.Equal(RuleName, resolved.RuleName);
        Assert.Equal(0, timestamp.Reads);
    }

    [Fact]
    public void Camera_metadata_on_the_same_day_as_the_name_is_not_flagged()
    {
        var metadata = Exif(new DateTime(2026, 6, 23, 8, 34, 32));

        var resolved = Resolve(metadata, fromCamera: true, Name(2026, 6, 23, new TimeOnly(10, 0, 0)));

        Assert.Equal(metadata, resolved.Date);
        Assert.False(resolved.DatesDiffer);
        Assert.Null(resolved.IgnoredFileNameDate);
    }

    [Fact]
    public void Camera_model_without_a_metadata_date_lets_the_name_win()
    {
        var resolved = Resolve(null, fromCamera: true, Name(2016, 7, 6));

        Assert.Equal(new CaptureDate(new DateTime(2016, 7, 6), DateSource.FileName), resolved.Date);
        Assert.False(resolved.DatesDiffer);
    }

    [Fact]
    public void A_null_timestamp_delegate_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CaptureDateResolver.Resolve(null, false, null, null!));
    }
}
