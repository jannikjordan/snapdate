using System.Globalization;
using Snapdate.Application.Ports;
using Snapdate.Application.Scanning;
using Snapdate.Application.Tests.Fakes;
using Snapdate.Domain.FileNames;
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

    // --- dates from file names (ADR 0007) --------------------------------------------------------

    /// <summary>When the sample files were downloaded (zip extraction sets this as the file timestamp).</summary>
    private static readonly DateTime Downloaded = new(2026, 11, 18, 8, 6, 0);

    private static readonly DateRuleSet WhatsApp = new(
    [
        new DateRule("whatsapp-android", "WhatsApp (Android)", RuleOrigin.System, Enabled: true, ["IMG-{YYYY}{MM}{DD}-WA*", "VID-{YYYY}{MM}{DD}-WA*"]),
    ]);

    /// <summary>"Now" is the download moment; local time is UTC so the test does not depend on the machine.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Downloaded, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static async Task<ScannedPhoto> ScanOne(string file, PhotoMetadata? metadata = null, DateRuleSet? rules = null)
    {
        var fs = new FakeFileSystem();
        var path = Path.Combine(Root, file);
        fs.AddFile(path, timestamp: Downloaded);
        var reader = new FakeMetadataReader();
        if (metadata is not null)
        {
            reader.Metadata[Path.GetFullPath(path)] = metadata;
        }

        var scan = new ScanSourceUseCase(fs, reader, rules ?? WhatsApp, new FixedClock());
        var list = new List<ScannedPhoto>();
        await foreach (var photo in scan.ScanAsync(Root, true, TestContext.Current.CancellationToken))
        {
            list.Add(photo);
        }

        return Assert.Single(list);
    }

    private static PhotoMetadata AppExif(DateTime taken, DateSource source = DateSource.ExifOriginal) => new(taken, source, null);

    [Theory]
    [InlineData("IMG-20260812-WA0017.jpg", "2026-08-12 20:44:14")]
    [InlineData("IMG-20260924-WA0003.jpg", "2026-09-24 18:28:51")]
    [InlineData("IMG-20261024-WA0028.jpg", "2026-10-24 16:31:41")]
    public async Task Same_day_app_metadata_is_kept(string file, string taken)
    {
        var value = DateTime.Parse(taken, CultureInfo.InvariantCulture);

        var photo = await ScanOne(file, AppExif(value));

        Assert.Equal(new CaptureDate(value, DateSource.ExifOriginal), photo.Date);
        Assert.Equal("WhatsApp (Android)", photo.ResolvedDate.RuleName);
        Assert.False(photo.ResolvedDate.DatesDiffer);
        Assert.Null(photo.ResolvedDate.IgnoredMetadataDate);
    }

    [Fact]
    public async Task Different_day_app_metadata_loses_to_the_file_name()
    {
        var photo = await ScanOne("IMG-20160706-WA0002.jpg", AppExif(new DateTime(2026, 6, 23, 8, 34, 32)));

        Assert.Equal(new CaptureDate(new DateTime(2016, 7, 6), DateSource.FileName), photo.Date);
        Assert.Equal("WhatsApp (Android)", photo.ResolvedDate.RuleName);
        Assert.Equal(new DateTime(2026, 6, 23, 8, 34, 32), photo.ResolvedDate.IgnoredMetadataDate);
        Assert.Null(photo.ResolvedDate.IgnoredFileNameDate);
        Assert.True(photo.ResolvedDate.DatesDiffer);
    }

    [Theory]
    [InlineData("IMG-20261110-WA0009.jpg", 2026, 11, 10)]
    [InlineData("IMG-20260801-WA0030.jpg", 2026, 8, 1)]
    [InlineData("IMG-20261117-WA0000.jpg", 2026, 11, 17)]
    public async Task Names_without_metadata_get_midnight_when_downloaded_on_another_day(string file, int year, int month, int day)
    {
        var photo = await ScanOne(file);

        Assert.Equal(new CaptureDate(new DateTime(year, month, day), DateSource.FileName), photo.Date);
        Assert.True(photo.Date.IsFromFileName);
        Assert.False(photo.ResolvedDate.DatesDiffer);
    }

    [Fact]
    public async Task Name_without_metadata_takes_the_time_of_a_same_day_download()
    {
        var photo = await ScanOne("IMG-20261118-WA0001.jpg");

        Assert.Equal(new CaptureDate(Downloaded, DateSource.FileName), photo.Date);
    }

    [Fact]
    public async Task Export_day_exif_datetime_loses_to_the_file_name()
    {
        var photo = await ScanOne("IMG-20260812-WA0017.jpg", AppExif(new DateTime(2026, 11, 18, 8, 0, 0), DateSource.ExifDateTime));

        Assert.Equal(new CaptureDate(new DateTime(2026, 8, 12), DateSource.FileName), photo.Date);
        Assert.Equal(new DateTime(2026, 11, 18, 8, 0, 0), photo.ResolvedDate.IgnoredMetadataDate);
        Assert.True(photo.ResolvedDate.DatesDiffer);
    }

    [Fact]
    public async Task Camera_metadata_wins_over_the_file_name_and_is_flagged()
    {
        var photo = await ScanOne("IMG-20160706-WA0002.jpg", new PhotoMetadata(new DateTime(2026, 6, 23, 8, 34, 32), DateSource.ExifOriginal, "Pixel 8"));

        Assert.Equal(new CaptureDate(new DateTime(2026, 6, 23, 8, 34, 32), DateSource.ExifOriginal), photo.Date);
        Assert.Equal(new FileNameDate(new DateOnly(2016, 7, 6), null), photo.ResolvedDate.IgnoredFileNameDate);
        Assert.Null(photo.ResolvedDate.IgnoredMetadataDate);
        Assert.True(photo.ResolvedDate.DatesDiffer);
        Assert.Equal("Pixel 8", photo.CameraModel);
    }

    [Fact]
    public async Task Without_rules_names_are_not_read()
    {
        var photo = await ScanOne("IMG-20160706-WA0002.jpg", rules: DateRuleSet.Empty);

        Assert.Equal(new CaptureDate(Downloaded, DateSource.FileTimestamp), photo.Date);
        Assert.Null(photo.ResolvedDate.RuleName);
        Assert.False(photo.ResolvedDate.DatesDiffer);
    }

    [Fact]
    public async Task Names_are_matched_without_the_extension()
    {
        var rules = new DateRuleSet([new DateRule("custom-0a1b2c3d", "Scanner", RuleOrigin.Custom, Enabled: true, ["Scan_{YYYY}-{MM}-{DD}"])]);

        var photo = await ScanOne("Scan_2024-07-14.jpg", rules: rules);

        Assert.Equal(new CaptureDate(new DateTime(2024, 7, 14), DateSource.FileName), photo.Date);
        Assert.Equal("Scanner", photo.ResolvedDate.RuleName);
    }

    [Fact]
    public async Task Name_dates_after_now_are_ignored()
    {
        var photo = await ScanOne("IMG-20261231-WA0001.jpg");

        Assert.Equal(new CaptureDate(Downloaded, DateSource.FileTimestamp), photo.Date);
    }
}
