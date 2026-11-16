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
        Closing += (_, _) => (DataContext as MainWindowViewModel)?.SaveSettings();
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
