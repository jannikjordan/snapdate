using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Snapdate.App.Localization;
using Snapdate.App.Services;
using Snapdate.Application.Planning;
using Snapdate.Application.Scanning;

namespace Snapdate.App.ViewModels;

public enum BadgeKind
{
    Camera,
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
        Badge = item.WasRenamed ? BadgeKind.Renamed : item.Photo.Date.IsFromMetadata ? BadgeKind.Camera : BadgeKind.FileDate;
        BadgeText = Badge switch
        {
            BadgeKind.Camera => l.BadgeCamera,
            BadgeKind.FileDate => l.BadgeFile,
            _ => l.BadgeRenamed,
        };
        BadgeTip = Badge switch
        {
            BadgeKind.Camera => l.TipCamera,
            BadgeKind.FileDate => l.TipFile,
            _ => l.TipRenamed,
        };
        IsVideo = SupportedFormats.Videos.Contains(item.Photo.Extension);
        // "Numbered" depends on the pattern; while it is invalid the row makes no such claim.
        ShowBadge = !(hideNumbered && Badge == BadgeKind.Renamed);
        Accessible = ShowBadge ? $"{OriginalName} → {NewName}, {BadgeText}" : $"{OriginalName} → {NewName}";
    }

    public RenameItem Item { get; }

    public string OriginalName { get; }

    public string NewName { get; }

    public BadgeKind Badge { get; }

    public bool IsCameraBadge => Badge == BadgeKind.Camera;

    public bool IsWarningBadge => Badge != BadgeKind.Camera;

    public bool ShowBadge { get; }

    public string BadgeText { get; }

    public string BadgeTip { get; }

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

    private async Task LoadThumbnailAsync()
    {
        var bitmap = await _thumbnails!.LoadAsync(Item.Photo.SourcePath, 96, CancellationToken.None).ConfigureAwait(false);
        if (bitmap is not null)
        {
            Dispatcher.UIThread.Post(() => Thumbnail = bitmap);
        }
    }
}
