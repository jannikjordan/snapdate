using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Snapdate.App.Tests.Support;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Application.FileNames;
using Snapdate.Domain.FileNames;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.App.Tests.Headless;

/// <summary>"Date from name": file-name date rules wired into scanning, badge, "Dates differ" hint and filter.</summary>
public sealed class FileNameDateTests(HeadlessSession headless)
{
    private static readonly DateTime FileDate = new(2026, 7, 1, 10, 0, 0);

    private Task RunOnUi(Func<Task> action) =>
        headless.Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            TestContext.Current.CancellationToken);

    /// <summary>Writes a JPEG; with <paramref name="taken"/> it has EXIF (from a camera when <paramref name="camera"/> is set).</summary>
    private static void Jpeg(TempFolder temp, string relative, DateTime? taken = null, string? camera = null)
    {
        var path = temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, taken is null ? [0xFF, 0xD8, 0xFF, 0xD9] : TestImages.JpegWithExif(taken, camera));
        File.SetCreationTime(path, FileDate);
        File.SetLastWriteTime(path, FileDate);
    }

    private static async Task<MainWindowViewModel> ScanAsync(TempFolder temp, string culture = "en-US", TestServices? services = null)
    {
        var vm = (services ?? new TestServices()).CreateViewModel(culture);
        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;
        vm.TargetFolder = temp.Combine("sorted");
        return vm;
    }

    [Fact]
    public Task WhatsApp_name_wins_over_contradicting_app_metadata_and_says_so() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        // A real case: an app wrote a later DateTimeOriginal (no camera model) into a WhatsApp picture.
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg", new DateTime(2026, 6, 23, 8, 34, 32));

        var vm = await ScanAsync(temp);
        var item = vm.VisibleItems.Single();

        Assert.Equal(BadgeKind.FileName, item.Badge);
        Assert.Equal("Date from name", item.BadgeText);
        Assert.True(item.IsInfoBadge);
        Assert.False(item.IsWarningBadge);
        Assert.StartsWith("2016-07-06", item.NewName, StringComparison.Ordinal);
        Assert.Contains("WhatsApp (Android)", item.BadgeTip, StringComparison.Ordinal);

        Assert.True(item.DatesDiffer);
        Assert.Equal("Dates differ", item.DatesDifferText);
        Assert.Contains("7/6/2016", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Contains("6/23/2026", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Contains("The name was used", item.DatesDifferTip, StringComparison.Ordinal);

        Assert.Contains("Date from name", item.Accessible, StringComparison.Ordinal);
        Assert.Contains("Dates differ", item.Accessible, StringComparison.Ordinal);
    });

    [Fact]
    public Task Camera_metadata_wins_over_the_name_and_the_hint_shows_the_ignored_name_date() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg", new DateTime(2016, 7, 9, 18, 0, 0), camera: "Pixel 8");

        var vm = await ScanAsync(temp);
        var item = vm.VisibleItems.Single();

        Assert.Equal(BadgeKind.Camera, item.Badge);
        Assert.StartsWith("2016-07-09", item.NewName, StringComparison.Ordinal);
        Assert.True(item.DatesDiffer);
        Assert.Contains("camera date", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Contains("7/6/2016", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Equal(0, vm.CountFileName);
        Assert.Equal(1, vm.CountCamera);
    });

    [Fact]
    public Task Same_day_metadata_shows_no_hint() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg", new DateTime(2016, 7, 6, 14, 5, 6));

        var item = (await ScanAsync(temp)).VisibleItems.Single();

        // Same day: the metadata date (with its more precise time) is kept as it is.
        Assert.Equal(BadgeKind.Camera, item.Badge);
        Assert.False(item.DatesDiffer);
        Assert.Empty(item.DatesDifferTip);
        Assert.DoesNotContain("Dates differ", item.Accessible, StringComparison.Ordinal);
        Assert.StartsWith("2016-07-06_14-05-06", item.NewName, StringComparison.Ordinal);
    });

    [Fact]
    public Task Each_date_source_has_its_own_count_and_filter_and_the_counts_add_up() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/DSC00012.jpg", new DateTime(2024, 7, 14, 16, 1, 55), camera: "ILCE-7M4");
        Jpeg(temp, "in/IMG-20240716-WA0003.jpg");
        Jpeg(temp, "in/WhatsApp Image 2024-07-15 at 19.05.22.jpg");
        Jpeg(temp, "in/Scan.jpg");

        var vm = await ScanAsync(temp);

        Assert.Equal(4, vm.CountAll);
        Assert.Equal(1, vm.CountCamera);
        Assert.Equal(2, vm.CountFileName);
        Assert.Equal(1, vm.CountFile);
        Assert.Equal(vm.CountAll, vm.CountCamera + vm.CountFileName + vm.CountFile);

        vm.IsFilterFileName = true;
        Assert.Equal(PreviewFilter.FileName, vm.Filter);
        Assert.True(vm.IsFilterFileName);
        Assert.False(vm.IsFilterFile);
        Assert.Equal(
            ["IMG-20240716-WA0003.jpg", "WhatsApp Image 2024-07-15 at 19.05.22.jpg"],
            vm.VisibleItems.Select(i => i.OriginalName).Order(StringComparer.Ordinal));
        Assert.All(vm.VisibleItems, i => Assert.Equal(BadgeKind.FileName, i.Badge));

        vm.IsFilterFile = true;
        Assert.Equal("Scan.jpg", vm.VisibleItems.Single().OriginalName);
        Assert.Equal(BadgeKind.FileDate, vm.VisibleItems.Single().Badge);

        vm.IsFilterCamera = true;
        Assert.Equal("DSC00012.jpg", vm.VisibleItems.Single().OriginalName);
    });

    [Fact]
    public Task German_badge_hint_and_dates_are_localised() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg", new DateTime(2026, 6, 23, 8, 34, 32));

        var vm = await ScanAsync(temp, "de-DE");
        var item = vm.VisibleItems.Single();

        Assert.Equal("Datum aus Name", vm.L.FilterFileName);
        Assert.Equal("Datum aus Name", item.BadgeText);
        Assert.Equal("Daten weichen ab", item.DatesDifferText);
        Assert.Contains("06.07.2016", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Contains("23.06.2026", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Contains("Der Name wurde verwendet", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Contains("nach der Regel „WhatsApp (Android)“", item.BadgeTip, StringComparison.Ordinal);
        Assert.Equal("Aufnahmedatum", vm.L.FilterCamera);
    });

    [Fact]
    public Task German_camera_case_speaks_of_the_capture_date() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg", new DateTime(2016, 7, 9, 18, 0, 0), camera: "Pixel 8");

        var item = (await ScanAsync(temp, "de-DE")).VisibleItems.Single();

        Assert.StartsWith("Das Aufnahmedatum (09.07.2016", item.DatesDifferTip, StringComparison.Ordinal);
        Assert.Contains("der Name sagt 06.07.2016", item.DatesDifferTip, StringComparison.Ordinal);
    });

    [Fact]
    public Task Custom_rules_are_used_and_named_in_the_tooltip() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/Scan_2023-02-01_7.jpg");
        var custom = new DateRule("custom-0a1b2c3d", "My scanner", RuleOrigin.Custom, Enabled: true, ["Scan_{YYYY}-{MM}-{DD}_*"]);
        var services = new TestServices { DateRules = new MemoryDateRuleStore(userRules: new UserDateRules([], [custom])) };

        var item = (await ScanAsync(temp, services: services)).VisibleItems.Single();

        Assert.Equal(BadgeKind.FileName, item.Badge);
        Assert.StartsWith("2023-02-01", item.NewName, StringComparison.Ordinal);
        Assert.Equal("The date was read from the file name using the rule \"My scanner\".", item.BadgeTip);
    });

    [Fact]
    public void Memory_rule_store_detects_changes_by_another_writer()
    {
        var store = new MemoryDateRuleStore();
        var first = DateRuleCatalog.Load(store);
        var second = DateRuleCatalog.Load(store);
        Assert.True(first.SetEnabled("whatsapp-android", false));
        Assert.True(second.SetEnabled("whatsapp-desktop", false));

        Assert.Equal(DateRuleSaveResult.Saved, first.Save(store));
        Assert.Equal(DateRuleSaveResult.Saved, first.Save(store));
        Assert.Equal(DateRuleSaveResult.Conflict, second.Save(store));

        // The same content written again by someone else is still a change.
        store.Write(store.User!);
        Assert.Equal(DateRuleSaveResult.Conflict, first.Save(store));
        Assert.Equal(2, store.Saves);
    }

    [Fact]
    public Task Switched_off_system_rules_are_not_used() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg");
        var services = new TestServices
        {
            DateRules = new MemoryDateRuleStore(userRules: new UserDateRules(["whatsapp-android"], [])),
        };

        var vm = await ScanAsync(temp, services: services);

        Assert.Equal(BadgeKind.FileDate, vm.VisibleItems.Single().Badge);
        Assert.Equal(0, vm.CountFileName);
        Assert.Equal(1, vm.CountFile);
    });

    [Fact]
    public Task Without_a_rule_store_no_dates_are_read_from_names() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg");

        var vm = await ScanAsync(temp, services: new TestServices { DateRules = null });

        Assert.Null(vm.DateRules);
        Assert.Equal(BadgeKind.FileDate, vm.VisibleItems.Single().Badge);
    });

    [Fact]
    public Task Window_shows_the_filter_tab_badge_and_hint() => RunOnUi(async () =>
    {
        using var temp = new TempFolder();
        Jpeg(temp, "in/IMG-20160706-WA0002.jpg", new DateTime(2026, 6, 23, 8, 34, 32));
        var vm = new TestServices().CreateViewModel("de-DE");
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 720 };
        window.Show();

        vm.SourceFolder = temp.Combine("in");
        await vm.ScanCompletion;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var texts = window.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
        Assert.Contains(texts, t => t.Inlines?.Text?.Contains("Datum aus Name", StringComparison.Ordinal) == true);
        Assert.Contains(texts, t => t.Text == "Datum aus Name");
        Assert.Contains(texts, t => t.Text == "Daten weichen ab");

        // The filter tabs wrap inside the preview header instead of being cut off.
        var tabs = window.GetLogicalDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("tab")).ToList();
        Assert.Equal(5, tabs.Count);
        var header = tabs[0].FindLogicalAncestorOfType<WrapPanel>()!;
        Assert.All(tabs.Where(t => t.IsVisible), t => Assert.True(t.Bounds.Right <= header.Bounds.Width + 0.5));
        window.Close();
    });
}
