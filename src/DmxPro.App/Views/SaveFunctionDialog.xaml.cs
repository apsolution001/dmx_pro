using System.Windows;
using System.Windows.Input;
using DmxPro.App.ViewModels;
using Wpf.Ui.Controls;

namespace DmxPro.App.Views;

/// <summary>
/// "Save as function" name dialog. Code-behind only handles focus and closing: Enter saves (after moving focus so a
/// number box being typed in commits its value), Esc cancels.
/// </summary>
public partial class SaveFunctionDialog : FluentWindow
{
    private readonly SaveFunctionDialogViewModel _viewModel;

    public SaveFunctionDialog(SaveFunctionDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // Enter on another focused button (Cancel, title bar ✕) keeps that button's own action.
            case Key.Enter when e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase || ReferenceEquals(e.OriginalSource, SaveButton):
                e.Handled = true;
                Accept();
                break;
            case Key.Escape:
                e.Handled = true;
                DialogResult = false;
                break;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        // A number box only commits its text when it loses focus.
        if (!SaveButton.IsKeyboardFocused)
        {
            SaveButton.Focus();
            Keyboard.Focus(SaveButton);
        }

        if (_viewModel.TryAccept())
        {
            DialogResult = true;
        }
        else
        {
            NameBox.Focus();
        }
    }
}
