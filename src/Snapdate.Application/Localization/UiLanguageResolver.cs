using System.Globalization;

namespace Snapdate.Application.Localization;

/// <summary>
/// Picks the UI language: an explicit user choice wins, otherwise the Windows
/// display language decides (German → German, anything else → English).
/// </summary>
public static class UiLanguageResolver
{
    /// <summary>Resolves the language to show.</summary>
    /// <param name="systemUiCulture">The OS display culture, usually <see cref="CultureInfo.CurrentUICulture"/>.</param>
    /// <param name="userChoice">A language the user picked earlier, if any.</param>
    public static UiLanguage Resolve(CultureInfo? systemUiCulture, UiLanguage? userChoice = null)
    {
        if (userChoice is { } chosen)
        {
            return chosen;
        }

        return string.Equals(systemUiCulture?.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.German
            : UiLanguage.English;
    }

    /// <summary>The culture used for resources and month/weekday names.</summary>
    public static CultureInfo ToCulture(UiLanguage language) => language switch
    {
        UiLanguage.German => CultureInfo.GetCultureInfo("de-DE"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };
}
