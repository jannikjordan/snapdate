using Snapdate.Application.Settings;
using Snapdate.Infrastructure.Settings;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.Infrastructure.Tests.Settings;

public sealed class UpdateSettingsRoundTripTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Update_check_defaults_to_on_with_no_history()
    {
        var settings = new JsonSettingsStore(_temp.Combine("none.json")).Load();
        Assert.True(settings.AutoUpdateCheck);
        Assert.Null(settings.LastUpdateCheckUtc);
        Assert.Null(settings.SkippedVersion);
    }

    [Fact]
    public void Update_check_settings_round_trip()
    {
        var store = new JsonSettingsStore(_temp.Combine("settings.json"));
        var saved = new AppSettings
        {
            AutoUpdateCheck = false,
            LastUpdateCheckUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            SkippedVersion = "0.2.0",
        };
        store.Save(saved);
        Assert.Equal(saved, store.Load());
    }

    [Fact]
    public void Settings_files_from_older_versions_load_with_update_check_on()
    {
        // Regression: fails if AppSettings uses init-only properties, because the source-generated
        // deserializer then drops the property defaults (IncludeSubfolders, AutoUpdateCheck) for missing keys.
        var path = _temp.Combine("old.json");
        File.WriteAllText(path, "{ \"Pattern\": \"{YYYY}\" }");
        var settings = new JsonSettingsStore(path).Load();
        Assert.Equal("{YYYY}", settings.Pattern);
        Assert.True(settings.IncludeSubfolders);
        Assert.True(settings.AutoUpdateCheck);
    }
}
