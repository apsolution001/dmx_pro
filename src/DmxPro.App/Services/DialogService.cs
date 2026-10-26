using DmxPro.App.ViewModels;
using DmxPro.App.Views;
using Wpf.Ui.Controls;

namespace DmxPro.App.Services;

public sealed class DialogService : IDialogService
{
    /// <summary>
    /// Every message box gets this minimum width: WPF-UI splits the width evenly between the buttons, so a box sized to
    /// a short question cut "Don't save" and long confirm labels off.
    /// </summary>
    private const double MessageBoxMinWidth = 420;

    public async Task ShowErrorAsync(string title, string message)
    {
        var box = NewMessageBox(title, message);
        box.CloseButtonText = "OK";
        await box.ShowDialogAsync();
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        var box = NewMessageBox(title, message);
        box.CloseButtonText = "Close";
        await box.ShowDialogAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = NewMessageBox(title, message);
        box.PrimaryButtonText = confirmText;
        box.CloseButtonText = "Cancel";
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    public bool ShowAddFixture(AddFixtureViewModel viewModel)
    {
        var window = new AddFixtureWindow(viewModel) { Owner = DialogOwner.ActiveOwner() };
        return window.ShowDialog() == true;
    }

    public bool ShowChannelLayout(ChannelLayoutViewModel viewModel)
    {
        // Opened from the Fixtures page or from the Add Fixture dialog: own it by whichever window is active.
        var window = new ChannelLayoutWindow(viewModel) { Owner = DialogOwner.ActiveOwner() };
        return window.ShowDialog() == true;
    }

    public bool ShowSaveFunction(SaveFunctionDialogViewModel viewModel)
    {
        var window = new SaveFunctionDialog(viewModel) { Owner = DialogOwner.ActiveOwner() };
        return window.ShowDialog() == true;
    }

    public async Task<SaveChangesChoice> AskSaveChangesAsync(string projectName)
    {
        var box = NewMessageBox("Unsaved changes", $"Save changes to \"{projectName}\"?");
        box.PrimaryButtonText = "Save";
        box.SecondaryButtonText = "Don't save";
        box.CloseButtonText = "Cancel";

        return await box.ShowDialogAsync() switch
        {
            MessageBoxResult.Primary => SaveChangesChoice.Save,
            MessageBoxResult.Secondary => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }

    // Owned by the active window (the Stage window may be on another monitor) and centred on it.
    private static MessageBox NewMessageBox(string title, string message) => new()
    {
        Title = title,
        Content = message,
        MinWidth = MessageBoxMinWidth,
        Owner = DialogOwner.ActiveOwner(),
        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
    };
}
