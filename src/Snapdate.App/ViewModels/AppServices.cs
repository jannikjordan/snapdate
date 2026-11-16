using Snapdate.App.Services;
using Snapdate.Application.Ports;

namespace Snapdate.App.ViewModels;

/// <summary>Everything the main view model depends on, wired in the composition root.</summary>
public sealed record AppServices(
    IThemeService Theme,
    IDesktopServices Desktop,
    ISettingsStore Settings,
    IFileSystem FileSystem,
    IMetadataReader MetadataReader,
    TimeProvider Clock,
    ThumbnailLoader? Thumbnails);
