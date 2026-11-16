// Hand-maintained: one property per key in Strings.resx.
// Tests verify every resx key has a property and both languages are complete.
using System.Globalization;
using System.Resources;

namespace Snapdate.App.Localization;

/// <summary>Strongly typed, AOT-safe access to UI strings for one culture.</summary>
public sealed class LocalizedStrings(CultureInfo culture)
{
    private static readonly ResourceManager Resources =
        new("Snapdate.App.Localization.Strings", typeof(LocalizedStrings).Assembly);

    /// <summary>Culture the strings are resolved for.</summary>
    public CultureInfo Culture { get; } = culture;

    public string AppTagline => Get(nameof(AppTagline));
    public string LanguageGroup => Get(nameof(LanguageGroup));
    public string ThemeToggle => Get(nameof(ThemeToggle));
    public string About => Get(nameof(About));
    public string Step1Title => Get(nameof(Step1Title));
    public string SourceLabel => Get(nameof(SourceLabel));
    public string TargetLabel => Get(nameof(TargetLabel));
    public string NotSelected => Get(nameof(NotSelected));
    public string IncludeSubfolders => Get(nameof(IncludeSubfolders));
    public string Step2Title => Get(nameof(Step2Title));
    public string ExampleLabel => Get(nameof(ExampleLabel));
    public string ExtensionNote => Get(nameof(ExtensionNote));
    public string Step3Title => Get(nameof(Step3Title));
    public string CopyTitle => Get(nameof(CopyTitle));
    public string CopyHint => Get(nameof(CopyHint));
    public string MoveTitle => Get(nameof(MoveTitle));
    public string MoveHint => Get(nameof(MoveHint));
    public string NeverOverwrite => Get(nameof(NeverOverwrite));
    public string PreviewTitle => Get(nameof(PreviewTitle));
    public string PreviewEmptyTitle => Get(nameof(PreviewEmptyTitle));
    public string PreviewEmptyBody => Get(nameof(PreviewEmptyBody));
    public string StatusChooseFolders => Get(nameof(StatusChooseFolders));
    public string StatusNothingChanged => Get(nameof(StatusNothingChanged));
    public string RunCopy => Get(nameof(RunCopy));
    public string RunMove => Get(nameof(RunMove));

    /// <summary>Looks up a key; falls back to the key itself so missing strings are visible, not fatal.</summary>
    public string Get(string key) => Resources.GetString(key, Culture) ?? key;
}
