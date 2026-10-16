using DmxPro.App.ViewModels;
using DmxPro.App.Views;
using Wpf.Ui.Controls;

namespace DmxPro.App.Services;

public sealed class DialogService : IDialogService
{
    public async Task ShowErrorAsync(string title, string message)
    {
        var box = new MessageBox { Title = title, Content = message, CloseButtonText = "OK" };
        await box.ShowDialogAsync();
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        var box = new MessageBox { Title = title, Content = message, CloseButtonText = "Close", MinWidth = 420 };
        await box.ShowDialogAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = new MessageBox { Title = title, Content = message, PrimaryButtonText = confirmText, CloseButtonText = "Cancel" };
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    public bool ShowAddFixture(AddFixtureViewModel viewModel)
    {
        var window = new AddFixtureWindow(viewModel) { Owner = System.Windows.Application.Current.MainWindow };
        return window.ShowDialog() == true;
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
