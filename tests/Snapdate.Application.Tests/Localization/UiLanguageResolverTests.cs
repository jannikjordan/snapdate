using System.Globalization;
using Snapdate.Application.Localization;

namespace Snapdate.Application.Tests.Localization;

public sealed class UiLanguageResolverTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("de-AT")]
    [InlineData("de-CH")]
    [InlineData("de")]
    public void German_system_language_selects_German(string culture)
    {
        Assert.Equal(UiLanguage.German, UiLanguageResolver.Resolve(CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    [InlineData("ja-JP")]
    public void Any_other_system_language_falls_back_to_English(string culture)
    {
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve(CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void Invariant_or_missing_culture_falls_back_to_English()
    {
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve(CultureInfo.InvariantCulture));
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve(null));
    }

    [Fact]
    public void Explicit_user_choice_wins_over_system_language()
    {
        Assert.Equal(UiLanguage.English, UiLanguageResolver.Resolve(CultureInfo.GetCultureInfo("de-DE"), UiLanguage.English));
        Assert.Equal(UiLanguage.German, UiLanguageResolver.Resolve(CultureInfo.GetCultureInfo("en-US"), UiLanguage.German));
    }

    [Fact]
    public void Languages_map_to_expected_cultures()
    {
        Assert.Equal("de", UiLanguageResolver.ToCulture(UiLanguage.German).TwoLetterISOLanguageName);
        Assert.Equal("en", UiLanguageResolver.ToCulture(UiLanguage.English).TwoLetterISOLanguageName);
    }
}
