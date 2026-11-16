using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Snapdate.App.Services;

/// <summary><see cref="IDesktopServices"/> backed by the window's storage provider and launcher.</summary>
public sealed class AvaloniaDesktopServices(Func<TopLevel?> topLevel) : IDesktopServices
{
    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        if (topLevel() is not { } top || !top.StorageProvider.CanPickFolder)
        {
            return null;
        }

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
        {
            start = await top.StorageProvider.TryGetFolderFromPathAsync(startFolder).ConfigureAwait(true);
        }

        var result = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        }).ConfigureAwait(true);

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task OpenUrlAsync(Uri url)
    {
        // Only ever open our own https links.
        if (url.Scheme != Uri.UriSchemeHttps || topLevel() is not { } top)
        {
            return;
        }

        await top.Launcher.LaunchUriAsync(url).ConfigureAwait(true);
    }

    public async Task OpenFolderAsync(string path)
    {
        if (Directory.Exists(path) && topLevel() is { } top)
        {
            var folder = await top.StorageProvider.TryGetFolderFromPathAsync(path).ConfigureAwait(true);
            if (folder is not null)
            {
                await top.Launcher.LaunchFileAsync(folder).ConfigureAwait(true);
            }
        }
    }

    public async Task OpenFileAsync(string path)
    {
        if (File.Exists(path) && topLevel() is { } top)
        {
            var file = await top.StorageProvider.TryGetFileFromPathAsync(path).ConfigureAwait(true);
            if (file is not null)
            {
                await top.Launcher.LaunchFileAsync(file).ConfigureAwait(true);
            }
        }
    }
}
