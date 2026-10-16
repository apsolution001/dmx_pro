using System.ComponentModel;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App;

public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private bool _closeConfirmed;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
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
}
