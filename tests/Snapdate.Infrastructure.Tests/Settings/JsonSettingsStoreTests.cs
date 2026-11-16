using Snapdate.Application.Localization;
using Snapdate.Application.Settings;
using Snapdate.Domain.Planning;
using Snapdate.Infrastructure.Settings;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Missing_file_yields_safe_defaults()
    {
        var settings = new JsonSettingsStore(_temp.Combine("none.json")).Load();
        Assert.Equal(TransferMode.Copy, settings.Mode);
        Assert.Equal("{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}", settings.Pattern);
        Assert.Null(settings.Language);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var path = _temp.Combine("sub", "settings.json");
        var store = new JsonSettingsStore(path);
        var saved = new AppSettings
        {
            SourceFolder = @"D:\Photos",
            TargetFolder = @"E:\Sorted",
            Pattern = "{YYYY}-{MM}-{DD} {HH}{mm}",
            IncludeSubfolders = false,
            Mode = TransferMode.Move,
            Language = UiLanguage.German,
            Theme = "dark",
        };

        store.Save(saved);

        Assert.Equal(saved, store.Load());
        Assert.Contains("\"German\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{\"Mode\": \"Explode\"}")]
    public void Corrupt_files_yield_defaults(string content)
    {
        var path = _temp.Write("settings.json", System.Text.Encoding.UTF8.GetBytes(content));
        Assert.Equal(new AppSettings(), new JsonSettingsStore(path).Load());
    }

    [Fact]
    public void Oversized_files_are_ignored()
    {
        var path = _temp.Write("settings.json", new byte[200 * 1024]);
        Assert.Equal(new AppSettings(), new JsonSettingsStore(path).Load());
    }
}
