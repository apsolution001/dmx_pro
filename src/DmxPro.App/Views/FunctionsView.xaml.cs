using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// Functions page. Code-behind only maps tree gestures (Enter, double-click, right-click select) to commands and
/// handles focus and menu placement (pure view concerns).
/// </summary>
public partial class FunctionsView : UserControl
{
    public FunctionsView()
    {
        InitializeComponent();
    }

    private FunctionsViewModel? ViewModel => DataContext as FunctionsViewModel;

    /// <summary>The toolbar "New" button opens its menu below itself on a normal click.</summary>
    private void OnNewMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>Ctrl+F focuses the search box and selects its text, so typing replaces the old search.</summary>
    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
        e.Handled = true;
    }

    /// <summary>Enter on a function opens the editor (type folders keep their default behaviour).</summary>
    private void OnFunctionTreePreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The view model's selection, not TreeView.SelectedItem: the tree is virtualized, so a row selected in code
        // while scrolled out of view has no container and the tree's SelectedItem can be stale.
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && e.OriginalSource is not TextBox
            && ViewModel?.SelectedRow is not null
            && ViewModel?.EditCommand is { } edit && edit.CanExecute(null))
        {
            edit.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Double-click on a function opens the editor.</summary>
    private void OnFunctionItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is TreeViewItem { DataContext: FunctionRowViewModel, IsSelected: true }
            && e.OriginalSource is not TextBox
            && ViewModel?.EditCommand is { } edit && edit.CanExecute(null))
        {
            edit.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Right-click selects the tree node under the mouse first, so the context menu acts on it.</summary>
    private void OnFunctionTreePreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
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
        if (sender is TextBox { DataContext: FunctionRowViewModel { IsEditing: true } row })
        {
            row.CommitRenameCommand.Execute(null);
        }
    }
}
