using System.Windows;
using System.Windows.Controls;
using DmxPro.App.ViewModels;
using DmxPro.Core.Profiles;

namespace DmxPro.App.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
    }

    // TreeView.SelectedItem cannot be bound; forward profile picks to the ViewModel.
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FixtureProfile profile && DataContext is ProfilesViewModel viewModel)
        {
            viewModel.SelectedProfile = profile;
        }
    }
}
