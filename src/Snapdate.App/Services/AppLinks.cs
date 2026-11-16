using Snapdate.Application.Localization;

namespace Snapdate.App.Services;

/// <summary>The only web addresses the app knows. Opened in the user's browser on request only.</summary>
public static class AppLinks
{
    public const string Repository = "https://github.com/jannikjordan/snapdate";

    /// <summary>The user guide in the current UI language.</summary>
    public static Uri UserGuide(UiLanguage language) => new(
        language == UiLanguage.German
            ? $"{Repository}/blob/main/docs/user-guide.de.md"
            : $"{Repository}/blob/main/docs/user-guide.en.md");

    /// <summary>Where portable users download the newest version.</summary>
    public static Uri LatestRelease { get; } = new(Repository + "/releases/latest");

    public static Uri Website { get; } = new(Repository);
}
