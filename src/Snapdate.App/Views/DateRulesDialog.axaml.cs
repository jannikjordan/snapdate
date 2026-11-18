using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Snapdate.App.ViewModels;

namespace Snapdate.App.Views;

/// <summary>
/// "Dates from file names" dialog. Keyboard handling only: focus moves into the dialog when it opens and back
/// when it closes, Esc cancels or closes, and after a duplicate or delete the focus lands on a sensible row.
/// </summary>
public partial class DateRulesDialog : UserControl
{
    private DateRulesViewModel? _viewModel;
    private IInputElement? _focusBeforeOpen;

    public DateRulesDialog()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Items.CollectionChanged -= OnItemsChanged;
        }

        _viewModel = DataContext as DateRulesViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Items.CollectionChanged += OnItemsChanged;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel is { IsOpen: true })
        {
            _viewModel.Escape();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DateRulesViewModel.IsOpen) || _viewModel is null)
        {
            return;
        }

        var focus = TopLevel.GetTopLevel(this)?.FocusManager;
        if (_viewModel.IsOpen)
        {
            _focusBeforeOpen = focus?.GetFocusedElement();
            // The overlay becomes visible with this change; focus once it is laid out.
            Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
        }
        else
        {
            var previous = _focusBeforeOpen;
            _focusBeforeOpen = null;
            if (previous is InputElement { IsEffectivelyVisible: true } element)
            {
                element.Focus(NavigationMethod.Tab);
            }
        }
    }

    private void FocusFirst()
    {
        // Top to bottom: the first rule's switch, otherwise the tester. The rows exist once the overlay is laid out.
        TopLevel.GetTopLevel(this)?.UpdateLayout();
        var first = RuleList.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault(t => t.IsEffectivelyEnabled);
        if (first is not null)
        {
            first.Focus(NavigationMethod.Tab);
        }
        else
        {
            TestNameBox.Focus(NavigationMethod.Tab);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel?.FocusRuleId is not null)
        {
            Dispatcher.UIThread.Post(FocusRequestedRule, DispatcherPriority.Loaded);
        }
    }

    private void FocusRequestedRule()
    {
        if (_viewModel?.FocusRuleId is not { } id)
        {
            return;
        }

        _viewModel.ClearFocusRequest();
        var item = _viewModel.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        if (item is null || RuleList.ContainerFromItem(item) is not Control container)
        {
            FocusFirst();
            return;
        }

        container.BringIntoView();
        var toggle = container.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault();
        if (toggle is not null && toggle.IsEffectivelyEnabled)
        {
            toggle.Focus(NavigationMethod.Tab);
        }
        else
        {
            CloseButton.Focus(NavigationMethod.Tab);
        }
    }
}
