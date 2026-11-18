using Snapdate.App.Services;
using Snapdate.Application.Ports;
using Snapdate.Application.Updates;

namespace Snapdate.App.ViewModels;

/// <summary>Everything the main view model depends on, wired in the composition root.</summary>
/// <remarks>Without <see cref="DateRules"/> no dates are read from file names.</remarks>
public sealed record AppServices(
    IThemeService Theme,
    IDesktopServices Desktop,
    ISettingsStore Settings,
    IFileSystem FileSystem,
    IMetadataReader MetadataReader,
    TimeProvider Clock,
    ThumbnailLoader? Thumbnails,
    UpdateCheckService? UpdateCheck = null,
    IInstallInfo? Install = null,
    IUpdateInstaller? UpdateInstaller = null,
    IAppExit? AppExit = null,
    IDateRuleStore? DateRules = null);
