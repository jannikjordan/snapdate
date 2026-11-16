using Avalonia;
using Avalonia.Controls;

namespace Snapdate.App.Controls;

/// <summary>The Snapdate logo as a resolution-independent vector control.</summary>
public partial class BrandLogo : UserControl
{
    /// <summary>When true, the small-size variant without sun and mountain is drawn (≤ 24 px).</summary>
    public static readonly StyledProperty<bool> IsSimplifiedProperty =
        AvaloniaProperty.Register<BrandLogo, bool>(nameof(IsSimplified));

    public BrandLogo()
    {
        InitializeComponent();
    }

    public bool IsSimplified
    {
        get => GetValue(IsSimplifiedProperty);
        set => SetValue(IsSimplifiedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSimplifiedProperty)
        {
            Sun.IsVisible = !IsSimplified;
            Mountain.IsVisible = !IsSimplified;
        }
    }
}
