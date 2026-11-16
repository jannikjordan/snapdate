using System.Globalization;
using Snapdate.Application.Planning;
using Snapdate.Application.Scanning;
using Snapdate.Application.Tests.Fakes;
using Snapdate.Domain.Naming;
using Snapdate.Domain.Photos;
using Snapdate.Domain.Planning;

namespace Snapdate.Application.Tests.Planning;

public sealed class BuildPlanUseCaseTests
{
    private static readonly string Source = Path.Combine(Path.GetTempPath(), "plan-src");
    private static readonly string Target = Path.Combine(Path.GetTempPath(), "plan-dst");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    private static ScannedPhoto Photo(string file, DateTime taken, DateSource source = DateSource.ExifOriginal) =>
        new(Path.Combine(Source, file), Path.GetFileNameWithoutExtension(file), Path.GetExtension(file), new CaptureDate(taken, source), null, 100);

    private static RenamePlan Build(FakeFileSystem fs, string pattern, params ScannedPhoto[] photos) =>
        new BuildPlanUseCase(fs).Build(photos, NamePattern.Parse(pattern).Pattern!, En, Source, Target, TransferMode.Copy);

    [Fact]
    public void Items_are_ordered_by_capture_date_and_numbered_in_that_order()
    {
        var plan = Build(
            new FakeFileSystem(),
            "{counter}",
            Photo("late.jpg", new DateTime(2024, 1, 3)),
            Photo("early.jpg", new DateTime(2024, 1, 1)),
            Photo("middle.jpg", new DateTime(2024, 1, 2)));

        Assert.Equal(["early", "middle", "late"], plan.Items.Select(i => i.Photo.OriginalName));
        Assert.Equal(["001.jpg", "002.jpg", "003.jpg"], plan.Items.Select(i => i.TargetRelativePath));
    }

    [Fact]
    public void Extension_is_kept_and_lower_cased()
    {
        var plan = Build(new FakeFileSystem(), "{YYYY}", Photo("A.JPG", new DateTime(2024, 1, 1)), Photo("B.HEIC", new DateTime(2025, 1, 1)));
        Assert.Equal(["2024.jpg", "2025.heic"], plan.Items.Select(i => i.TargetRelativePath));
    }

    [Fact]
    public void Photos_taken_in_the_same_second_get_numbered_suffixes()
    {
        var same = new DateTime(2024, 7, 14, 16, 1, 55);
        var plan = Build(new FakeFileSystem(), NamePattern.DefaultText, Photo("DSC1.jpg", same), Photo("DSC2.jpg", same), Photo("DSC3.jpg", same));

        Assert.Equal(
            ["2024-07-14_16-01-55.jpg", "2024-07-14_16-01-55 (2).jpg", "2024-07-14_16-01-55 (3).jpg"],
            plan.Items.Select(i => i.TargetRelativePath));
        Assert.Equal(2, plan.RenamedCount);
    }

    [Fact]
    public void Files_already_in_the_target_folder_are_never_overwritten()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(Path.Combine(Target, "2024.jpg"));

        var item = Assert.Single(Build(fs, "{YYYY}", Photo("a.jpg", new DateTime(2024, 1, 1))).Items);

        Assert.Equal("2024 (2).jpg", item.TargetRelativePath);
        Assert.True(item.WasRenamed);
    }

    [Fact]
    public void Sub_folder_patterns_produce_relative_paths()
    {
        var item = Assert.Single(Build(new FakeFileSystem(), "{YYYY}/{MM}/{DD}", Photo("a.jpg", new DateTime(2024, 7, 4))).Items);
        Assert.Equal("2024/07/04.jpg", item.TargetRelativePath);
        Assert.Equal("04.jpg", item.TargetFileName);
    }

    [Fact]
    public void Counts_reflect_where_dates_came_from()
    {
        var plan = Build(
            new FakeFileSystem(),
            "{name}",
            Photo("a.jpg", new DateTime(2024, 1, 1)),
            Photo("b.jpg", new DateTime(2024, 1, 2), DateSource.FileTimestamp));

        Assert.Equal(1, plan.FromMetadataCount);
        Assert.Equal(1, plan.FromFileDateCount);
    }
}
