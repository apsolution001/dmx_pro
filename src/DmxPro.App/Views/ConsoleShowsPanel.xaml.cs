using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Views;

/// <summary>
/// Shows list of the Virtual Console. Code-behind only routes input: Enter and a double-click on a row play the
/// selected show (the DataGrid would otherwise use Enter to move to the next row).
/// </summary>
public partial class ConsoleShowsPanel : UserControl
{
    public ConsoleShowsPanel()
    {
        InitializeComponent();
    }

    private ShowPlayerViewModel? Vm => DataContext as ShowPlayerViewModel;

    private void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && Vm is { } vm)
        {
            vm.PlaySelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only rows play; a double-click on a column header (auto-size) or on the row's own play button must not.
        if (Vm is { } vm && e.OriginalSource is DependencyObject source && !IsInsideButton(source)
            && ItemsControl.ContainerFromElement(ShowsGrid, source) is DataGridRow)
        {
            vm.PlaySelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null and not DataGridRow)
        {
            if (element is ButtonBase)
            {
                return true;
            }

            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShowsGrid.SelectedItem is { } item)
        {
            ShowsGrid.ScrollIntoView(item);
        }
    }
}
