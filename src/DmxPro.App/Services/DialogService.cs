using System.Windows.Interop;
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

    /// <summary>Prompts of this service that are open right now (they can nest: a prompt over a dialog).</summary>
    private int _open;

    /// <summary>
    /// Also true for modal windows shown elsewhere (file dialogs, <c>ShowDialog</c>): WPF marks the thread modal while
    /// one runs.
    /// </summary>
    public bool IsModalOpen => _open > 0 || ComponentDispatcher.IsThreadModal;

    public async Task ShowErrorAsync(string title, string message)
    {
        var box = NewMessageBox(title, message);
        box.CloseButtonText = "OK";
        await ShowAsync(box);
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        var box = NewMessageBox(title, message);
        box.CloseButtonText = "Close";
        await ShowAsync(box);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = NewMessageBox(title, message);
        box.PrimaryButtonText = confirmText;
        box.CloseButtonText = "Cancel";
        return await ShowAsync(box) == MessageBoxResult.Primary;
    }

    public bool ShowAddFixture(AddFixtureViewModel viewModel) =>
        ShowModal(new AddFixtureWindow(viewModel) { Owner = DialogOwner.ActiveOwner() });

    // Opened from the Fixtures page or from the Add Fixture dialog: owned by whichever window is active.
    public bool ShowChannelLayout(ChannelLayoutViewModel viewModel) =>
        ShowModal(new ChannelLayoutWindow(viewModel) { Owner = DialogOwner.ActiveOwner() });

    public bool ShowSaveFunction(SaveFunctionDialogViewModel viewModel) =>
        ShowModal(new SaveFunctionDialog(viewModel) { Owner = DialogOwner.ActiveOwner() });

    public async Task<SaveChangesChoice> AskSaveChangesAsync(string projectName)
    {
        var box = NewMessageBox("Unsaved changes", $"Save changes to \"{projectName}\"?");
        box.PrimaryButtonText = "Save";
        box.SecondaryButtonText = "Don't save";
        box.CloseButtonText = "Cancel";

        return await ShowAsync(box) switch
        {
            MessageBoxResult.Primary => SaveChangesChoice.Save,
            MessageBoxResult.Secondary => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }

    public async Task<ExternalChangeChoice> AskReloadExternalChangeAsync(string fileName)
    {
        var box = NewMessageBox(
            "Project changed on disk",
            $"The project file \"{fileName}\" was changed outside DMX Pro.\n\n" +
            "Reload: use the file's version. Your unsaved changes here are replaced; Ctrl+Z brings them back.\n" +
            "Keep mine: keep working on your version. The next save overwrites the file.\n\n" +
            "Output and running lights stay as they are either way.");
        box.PrimaryButtonText = "Reload";
        box.CloseButtonText = "Keep mine";

        // Closing the box (Esc, X) keeps the user's work: nothing is lost that way.
        return await ShowAsync(box) == MessageBoxResult.Primary ? ExternalChangeChoice.Reload : ExternalChangeChoice.KeepMine;
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

    private async Task<MessageBoxResult> ShowAsync(MessageBox box)
    {
        _open++;
        try
        {
            return await box.ShowDialogAsync();
        }
        finally
        {
            _open--;
        }
    }

    private bool ShowModal(System.Windows.Window window)
    {
        _open++;
        try
        {
            return window.ShowDialog() == true;
        }
        finally
        {
            _open--;
        }
    }
}
