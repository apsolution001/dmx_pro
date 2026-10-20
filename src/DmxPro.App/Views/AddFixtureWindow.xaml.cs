using System.Windows;
using System.Windows.Input;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App.Views;

public partial class AddFixtureWindow : FluentWindow
{
    private readonly AddFixtureViewModel _viewModel;

    public AddFixtureWindow(AddFixtureViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    // TreeView.SelectedItem cannot be bound; forward model picks to the ViewModel. Manufacturer rows and the
    // empty selection after filtering keep the current profile.
    private void OnProfileTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ProfileTreeItem item)
        {
            _viewModel.SelectedItem = item;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        // Enter in a number box clicks the default button before the box has committed its value: move focus first
        // so the binding updates, then re-check the (possibly changed) validation.
        if (!AddButton.IsKeyboardFocused)
        {
            AddButton.Focus();
            Keyboard.Focus(AddButton);
        }

        if (_viewModel.CanAdd)
        {
            DialogResult = true;
        }
    }
}
