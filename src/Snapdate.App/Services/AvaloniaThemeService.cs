using Avalonia.Styling;

namespace Snapdate.App.Services;

/// <summary>Theme switching backed by <see cref="Avalonia.Application.RequestedThemeVariant"/>.</summary>
public sealed class AvaloniaThemeService(Avalonia.Application application) : IThemeService
{
    public bool IsDark => application.ActualThemeVariant == ThemeVariant.Dark;

    public void Toggle() =>
        application.RequestedThemeVariant = IsDark ? ThemeVariant.Light : ThemeVariant.Dark;
}
