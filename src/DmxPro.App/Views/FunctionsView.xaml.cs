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
        if (e.OriginalSource is TextBoxBase box && HandleRenameBoxShortcut(box, e))
        {
            return;
        }

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

    /// <summary>
    /// Inside the inline rename box, Ctrl+X/C/V edit the text and never reach the tree's function Cut/Copy/Paste
    /// bindings: a TextBox leaves those keys unhandled when it cannot run them (Ctrl+X with no selection, Ctrl+V with
    /// no clipboard text), so they bubbled up and cut (delete prompt) or pasted a whole function. Ctrl+D (duplicate)
    /// is swallowed for the same reason. Returns true when the key was taken here.
    /// </summary>
    private static bool HandleRenameBoxShortcut(TextBoxBase box, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.X or Key.C or Key.V or Key.D))
        {
            return false;
        }

        RoutedUICommand? command = e.Key switch
        {
            Key.X => ApplicationCommands.Cut,
            Key.C => ApplicationCommands.Copy,
            Key.V => ApplicationCommands.Paste,
            _ => null,
        };

        if (command is not null && command.CanExecute(null, box))
        {
            command.Execute(null, box);
        }

        e.Handled = true;
        return true;
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
