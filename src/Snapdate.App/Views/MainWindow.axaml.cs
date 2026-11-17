using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Snapdate.App.ViewModels;

namespace Snapdate.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WireDropTarget(SourceDrop, asSource: true);
        WireDropTarget(TargetDrop, asSource: false);
        Closing += (_, _) =>
        {
            (DataContext as MainWindowViewModel)?.Shutdown();
            (DataContext as MainWindowViewModel)?.SaveSettings();
        };
    }

    /// <summary>
    /// Snapdate has no fullscreen mode (it hides the caption buttons and the user could not leave it):
    /// anything that asks for <see cref="WindowState.FullScreen"/> gets the previous state back.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && change.NewValue is WindowState.FullScreen)
        {
            WindowState = change.OldValue is WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }
    }

    private void WireDropTarget(Button target, bool asSource)
    {
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Link : DragDropEffects.None;
            target.Classes.Set("dragover", e.DragEffects != DragDropEffects.None);
            e.Handled = true;
        });
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => target.Classes.Set("dragover", false));
        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            target.Classes.Set("dragover", false);
            var path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
            if (path is not null && DataContext is MainWindowViewModel vm)
            {
                vm.DropFolder(path, asSource);
            }

            e.Handled = true;
        });
    }
}
