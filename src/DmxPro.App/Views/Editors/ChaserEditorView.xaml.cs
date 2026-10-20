using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DmxPro.App.ViewModels.Editors;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// Chaser editor. Code-behind only handles focus (picker flyout, row selection when a timing box is focused) and maps
/// a double-click in the picker to the Add command (pure view concerns). DataContext: <c>ChaserEditorViewModel</c>.
/// </summary>
public partial class ChaserEditorView : UserControl
{
    public ChaserEditorView()
    {
        InitializeComponent();
    }

    private ChaserEditorViewModel? ViewModel => DataContext as ChaserEditorViewModel;

    /// <summary>Typing in a step's timing box selects that step, so the toolbar buttons act on it.</summary>
    private void OnRowGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DataGridRow { IsSelected: false } row)
        {
            row.IsSelected = true;
        }
    }

    /// <summary>A popup does not take focus by itself; put it in the search box.</summary>
    private void OnAddPopupOpened(object? sender, EventArgs e) =>
        PickerSearchBox.Dispatcher.BeginInvoke(() => PickerSearchBox.Focus());

    /// <summary>Esc closes the flyout and returns focus to the Add button.</summary>
    private void OnAddPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AddPopup.IsOpen = false;
            AddToggle.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Down arrow in the search box moves into the list (first item selected).</summary>
    private void OnPickerSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && PickerList.Items.Count > 0)
        {
            if (PickerList.SelectedIndex < 0)
            {
                PickerList.SelectedIndex = 0;
            }

            if (PickerList.ItemContainerGenerator.ContainerFromIndex(PickerList.SelectedIndex) is UIElement item)
            {
                item.Focus();
            }

            e.Handled = true;
        }
    }

    /// <summary>Double-click on a function adds it as a step.</summary>
    private void OnPickerItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is ListBoxItem { DataContext: FunctionPickerItem item }
            && ViewModel?.AddStepsCommand is { } add && add.CanExecute(item))
        {
            add.Execute(item);
            e.Handled = true;
        }
    }
}
