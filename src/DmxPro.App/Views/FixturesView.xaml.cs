using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// Fixtures page. Code-behind only handles focus, menu placement, tree hit-testing and keeping the table's selection in
/// step with <see cref="FixtureRowViewModel.IsSelected"/> (pure view concerns).
/// </summary>
public partial class FixturesView : UserControl
{
    private FixturesViewModel? _viewModel;

    /// <summary>True while the table selection is updated from the ViewModel (its events must not echo back).</summary>
    private bool _syncingSelection;

    public FixturesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.TableSelectionChanged -= OnTableSelectionChanged;
        }

        _viewModel = e.NewValue as FixturesViewModel;
        if (_viewModel is not null)
        {
            _viewModel.TableSelectionChanged += OnTableSelectionChanged;

            // The table's ItemsSource binding may not have picked up the new DataContext yet: sync once it has.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => OnTableSelectionChanged(_viewModel, EventArgs.Empty));
        }
    }

    /// <summary>
    /// The ViewModel selected rows itself (a group, a rebuild): make the table's selection match. The table virtualizes
    /// its rows, so selection lives in <see cref="System.Windows.Controls.Primitives.MultiSelector.SelectedItems"/>
    /// (by item) rather than in row containers that may not exist.
    /// </summary>
    private void OnTableSelectionChanged(object? sender, EventArgs e)
    {
        // Only rows of the bound collection can be selected (an item the grid does not show cannot be added).
        if (_viewModel is null || !ReferenceEquals(FixtureGrid.ItemsSource, _viewModel.Fixtures))
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            var selected = FixtureGrid.SelectedItems;
            var present = new HashSet<FixtureRowViewModel>();
            for (var i = selected.Count - 1; i >= 0; i--)
            {
                if (selected[i] is FixtureRowViewModel { IsSelected: true } row)
                {
                    present.Add(row);
                }
                else
                {
                    selected.RemoveAt(i);
                }
            }

            foreach (var row in _viewModel.Fixtures)
            {
                if (row.IsSelected && present.Add(row))
                {
                    selected.Add(row);
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        if (FixtureGrid.SelectedItem is { } item)
        {
            FixtureGrid.ScrollIntoView(item);
        }
    }

    /// <summary>F2 in the table jumps to the Name field of the Properties panel.</summary>
    private void OnFixtureGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None && NameBox.IsVisible)
        {
            NameBox.Focus();
            NameBox.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Copies the user's selection (click, Shift/Ctrl, Ctrl+A) to the rows' <see cref="FixtureRowViewModel.IsSelected"/>
    /// and keeps the focused row visible when the selection changes from elsewhere (a group, the address map).
    /// </summary>
    private void OnFixtureGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, FixtureGrid) || _syncingSelection)
        {
            return;
        }

        foreach (var item in e.RemovedItems)
        {
            if (item is FixtureRowViewModel row)
            {
                row.IsSelected = false;
            }
        }

        foreach (var item in e.AddedItems)
        {
            if (item is FixtureRowViewModel row)
            {
                row.IsSelected = true;
            }
        }

        if (FixtureGrid.SelectedItem is { } focused)
        {
            FixtureGrid.ScrollIntoView(focused);
        }
    }

    /// <summary>The toolbar "Group" button opens its menu below itself on a normal click.</summary>
    private void OnGroupMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>Right-click selects the tree node under the mouse first, so the context menu acts on it.</summary>
    private void OnGroupTreePreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not TreeViewItem)
        {
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        if (source is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    /// <summary>The inline rename box takes focus and selects its text when it appears.</summary>
    private void OnRenameBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            box.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    /// <summary>Clicking elsewhere commits the rename, like Explorer.</summary>
    private void OnRenameBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: GroupRowViewModel { IsEditing: true } group })
        {
            group.CommitRenameCommand.Execute(null);
        }
    }
}
