using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Snapdate.App.Localization;
using Snapdate.App.Services;
using Snapdate.Application.Planning;
using Snapdate.Application.Scanning;
using Snapdate.Domain.Photos;

namespace Snapdate.App.ViewModels;

public enum BadgeKind
{
    Camera,
    FileName,
    FileDate,
    Renamed,
}

/// <summary>One row in the preview list: old name → new name, plus where the date came from.</summary>
public sealed partial class PreviewItemViewModel : ObservableObject
{
    private readonly ThumbnailLoader? _thumbnails;
    private bool _thumbnailRequested;

    public PreviewItemViewModel(RenameItem item, LocalizedStrings l, ThumbnailLoader? thumbnails, bool hideNumbered = false)
    {
        _thumbnails = thumbnails;
        Item = item;
        OriginalName = item.Photo.OriginalName + item.Photo.Extension;
        NewName = item.TargetRelativePath.Replace('/', '\\');
        var resolved = item.Photo.ResolvedDate;
        Badge = item.WasRenamed ? BadgeKind.Renamed : BadgeFor(resolved.Date);
        BadgeText = Badge switch
        {
            BadgeKind.Camera => l.BadgeCamera,
            BadgeKind.FileName => l.BadgeFileName,
            BadgeKind.FileDate => l.BadgeFile,
            _ => l.BadgeRenamed,
        };
        BadgeTip = Badge switch
        {
            BadgeKind.Camera => l.TipCamera,
            BadgeKind.FileName => resolved.RuleName is { } rule
                ? l.Format(nameof(LocalizedStrings.TipFileNameRule), rule)
                : l.TipFileName,
            BadgeKind.FileDate => l.TipFile,
            _ => l.TipRenamed,
        };
        IsVideo = SupportedFormats.Videos.Contains(item.Photo.Extension);
        // "Numbered" depends on the pattern; while it is invalid the row makes no such claim.
        ShowBadge = !(hideNumbered && Badge == BadgeKind.Renamed);
        DatesDiffer = resolved.DatesDiffer;
        DatesDifferText = l.DatesDiffer;
        DatesDifferTip = DatesDiffer ? DescribeDifference(resolved, l) : string.Empty;

        var accessible = $"{OriginalName} → {NewName}";
        if (ShowBadge)
        {
            accessible += $", {BadgeText}";
        }

        if (DatesDiffer)
        {
            accessible += $", {l.DatesDiffer}: {DatesDifferTip}";
        }

        Accessible = accessible;
    }

    public RenameItem Item { get; }

    public string OriginalName { get; }

    public string NewName { get; }

    public BadgeKind Badge { get; }

    public bool IsCameraBadge => Badge == BadgeKind.Camera;

    /// <summary>"Date from name": primary (info) colour.</summary>
    public bool IsInfoBadge => Badge == BadgeKind.FileName;

    public bool IsWarningBadge => Badge is BadgeKind.FileDate or BadgeKind.Renamed;

    public bool ShowBadge { get; }

    public string BadgeText { get; }

    public string BadgeTip { get; }

    /// <summary>Metadata and file name give different days; the hint next to the badge explains which one was used.</summary>
    public bool DatesDiffer { get; }

    /// <summary>"Dates differ" in the current language.</summary>
    public string DatesDifferText { get; }

    /// <summary>Both dates, which one was used and why; empty when the dates agree.</summary>
    public string DatesDifferTip { get; }

    public bool IsVideo { get; }

    public string Accessible { get; }

    /// <summary>Loaded lazily the first time a visible row asks for it.</summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested && _thumbnails is not null && ThumbnailLoader.CanDecode(Item.Photo.SourcePath))
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync();
            }

            return field;
        }

        private set => SetProperty(ref field, value);
    }

    private static BadgeKind BadgeFor(CaptureDate date) =>
        date.IsFromFileName ? BadgeKind.FileName
        : date.IsFromMetadata ? BadgeKind.Camera
        : BadgeKind.FileDate;

    private static string DescribeDifference(ResolvedDate resolved, LocalizedStrings l)
    {
        var culture = l.Culture;
        if (resolved.IgnoredMetadataDate is { } metadata)
        {
            // The name won over metadata written by an app. Its day is what counts (the time may be the file's).
            return l.Format(
                nameof(LocalizedStrings.DatesDifferNameUsed),
                resolved.Date.Value.ToString("d", culture),
                metadata.ToString("g", culture));
        }

        if (resolved.IgnoredFileNameDate is { } name)
        {
            // Camera metadata always wins; the name may give only a day.
            var nameText = name.Time is null
                ? name.Date.ToString("d", culture)
                : name.ToDateTime().ToString("g", culture);
            return l.Format(nameof(LocalizedStrings.DatesDifferCameraUsed), resolved.Date.Value.ToString("g", culture), nameText);
        }

        return string.Empty;
    }

    private async Task LoadThumbnailAsync()
    {
        var bitmap = await _thumbnails!.LoadAsync(Item.Photo.SourcePath, 96, CancellationToken.None).ConfigureAwait(false);
        if (bitmap is not null)
        {
            Dispatcher.UIThread.Post(() => Thumbnail = bitmap);
        }
    }
}
