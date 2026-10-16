using System.Windows;
using DmxPro.App.ViewModels;
using DmxPro.Core.Profiles;
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
    }

    // TreeView.SelectedItem cannot be bound; forward profile picks to the ViewModel.
    private void OnProfileTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FixtureProfile profile)
        {
            _viewModel.SelectedProfile = profile;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
