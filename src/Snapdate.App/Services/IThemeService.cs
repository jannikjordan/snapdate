namespace Snapdate.App.Services;

/// <summary>Switches between light and dark appearance.</summary>
public interface IThemeService
{
    /// <summary>True when the dark theme is currently shown.</summary>
    bool IsDark { get; }

    /// <summary>Switches to the other theme.</summary>
    void Toggle();

    /// <summary>Applies a remembered choice: "light", "dark" or null (follow Windows).</summary>
    void Apply(string? theme);

    /// <summary>The explicit choice to remember, or null when following Windows.</summary>
    string? CurrentChoice { get; }
}
