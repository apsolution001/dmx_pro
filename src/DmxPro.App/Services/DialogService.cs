using Wpf.Ui.Controls;

namespace DmxPro.App.Services;

public sealed class DialogService : IDialogService
{
    public async Task ShowErrorAsync(string title, string message)
    {
        var box = new MessageBox { Title = title, Content = message, CloseButtonText = "OK" };
        await box.ShowDialogAsync();
    }

    public async Task<SaveChangesChoice> AskSaveChangesAsync(string projectName)
    {
        var box = new MessageBox
        {
            Title = "Unsaved changes",
            Content = $"Save changes to \"{projectName}\"?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
        };

        return await box.ShowDialogAsync() switch
        {
            MessageBoxResult.Primary => SaveChangesChoice.Save,
            MessageBoxResult.Secondary => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }
}
