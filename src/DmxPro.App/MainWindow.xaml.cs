using System.ComponentModel;
using System.Windows;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App;

public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private bool _closeConfirmed;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.CloseRequested += (_, _) => Close();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed)
        {
            return;
        }

        // Closing must wait for the async "save changes?" prompt, so cancel now and close again afterwards.
        e.Cancel = true;
        if (await _viewModel.ConfirmDiscardChangesAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsFullScreen))
        {
            ApplyFullScreen(_viewModel.IsFullScreen);
        }
    }

    /// <summary>Full screen is a pure view concern: hide the title bar and maximize.</summary>
    private void ApplyFullScreen(bool fullScreen)
    {
        if (fullScreen)
        {
            _stateBeforeFullScreen = WindowState;
            AppTitleBar.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Normal; // re-maximize so the window covers the whole screen
            WindowState = WindowState.Maximized;
        }
        else
        {
            AppTitleBar.Visibility = Visibility.Visible;
            WindowState = _stateBeforeFullScreen;
        }
    }
}
