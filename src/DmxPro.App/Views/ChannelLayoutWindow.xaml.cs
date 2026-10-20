using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App.Views;

/// <summary>Channel layout editor window. Code-behind only closes the window and keeps row selection in sync with focus.</summary>
public partial class ChannelLayoutWindow : FluentWindow
{
    private readonly ChannelLayoutViewModel _viewModel;
    private bool _closeConfirmed;

    public ChannelLayoutWindow(ChannelLayoutViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.CloseRequested += OnCloseRequested;
        Closing += OnClosing;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
        Loaded += (_, _) =>
        {
            ModelBox.Focus();
            ModelBox.SelectAll();
        };
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        if (saved)
        {
            _closeConfirmed = true;
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }

    /// <summary>Esc, Cancel and the title bar ✕ all ask before unsaved edits are thrown away.</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !_viewModel.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        if (await _viewModel.CanCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>Typing in a cell selects its row, so the row commands (Del, Alt+Up, …) act on it.</summary>
    private void OnRowGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DataGridRow { IsSelected: false } row)
        {
            row.IsSelected = true;
        }
    }
}
