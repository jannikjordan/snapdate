using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Snapdate.App.Localization;
using Snapdate.App.Services;
using Snapdate.Application.Localization;

namespace Snapdate.App.ViewModels;

/// <summary>State of the main window. M1: shell only (language, theme, copy/move).</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IThemeService _theme;

    public MainWindowViewModel(IThemeService theme, CultureInfo systemUiCulture)
    {
        _theme = theme;
        Language = UiLanguageResolver.Resolve(systemUiCulture);
        L = CreateStrings(Language);
    }

    /// <summary>All UI strings for the current language.</summary>
    [ObservableProperty]
    public partial LocalizedStrings L { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnglish), nameof(IsGerman))]
    public partial UiLanguage Language { get; set; }

    /// <summary>Copy is the safe default; move is opt-in.</summary>
    [ObservableProperty]
    public partial bool IsCopyMode { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = true;

    public bool IsEnglish
    {
        get => Language == UiLanguage.English;
        set { if (value) { Language = UiLanguage.English; } }
    }

    public bool IsGerman
    {
        get => Language == UiLanguage.German;
        set { if (value) { Language = UiLanguage.German; } }
    }

    public string VersionLabel { get; } = "v" + GetVersion();

    /// <summary>Placeholder example until the pattern builder lands (M4).</summary>
    public string ExampleFileName { get; } = "2024-07-14_15-32-08.jpg";

    public string DefaultPattern { get; } = "{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}";

    [RelayCommand]
    private void ToggleTheme() => _theme.Toggle();

    partial void OnLanguageChanged(UiLanguage value)
    {
        var culture = UiLanguageResolver.ToCulture(value);
        CultureInfo.CurrentUICulture = culture;
        L = CreateStrings(value);
    }

    private static LocalizedStrings CreateStrings(UiLanguage language) =>
        new(UiLanguageResolver.ToCulture(language));

    private static string GetVersion()
    {
        var info = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? info[..plus] : info;
    }
}
