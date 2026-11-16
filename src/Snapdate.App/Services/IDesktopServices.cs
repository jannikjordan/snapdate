namespace Snapdate.App.Services;

/// <summary>Operating-system interactions the view model needs (abstracted for tests).</summary>
public interface IDesktopServices
{
    /// <summary>Shows the folder picker; returns the chosen local path or null.</summary>
    Task<string?> PickFolderAsync(string title, string? startFolder);

    /// <summary>Opens a web page in the default browser.</summary>
    Task OpenUrlAsync(Uri url);

    /// <summary>Opens a folder in Explorer.</summary>
    Task OpenFolderAsync(string path);

    /// <summary>Opens a file with its default program.</summary>
    Task OpenFileAsync(string path);
}
