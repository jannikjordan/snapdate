using Avalonia.Styling;

namespace Snapdate.App.Services;

/// <summary>Theme switching backed by <see cref="Avalonia.Application.RequestedThemeVariant"/>.</summary>
public sealed class AvaloniaThemeService(Avalonia.Application application) : IThemeService
{
    public bool IsDark => application.ActualThemeVariant == ThemeVariant.Dark;

    public string? CurrentChoice =>
        application.RequestedThemeVariant == ThemeVariant.Dark ? "dark"
        : application.RequestedThemeVariant == ThemeVariant.Light ? "light"
        : null;

    public void Toggle() =>
        application.RequestedThemeVariant = IsDark ? ThemeVariant.Light : ThemeVariant.Dark;

    public void Apply(string? theme) =>
        application.RequestedThemeVariant = theme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
}
