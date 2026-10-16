namespace DmxPro.App.Services;

/// <summary>Result of an "unsaved changes" prompt.</summary>
public enum SaveChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Modal prompts, behind an interface so ViewModels stay testable.</summary>
public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);

    Task ShowInfoAsync(string title, string message);

    Task<SaveChangesChoice> AskSaveChangesAsync(string projectName);

    /// <summary>Asks a yes/no question; returns true when the user picks <paramref name="confirmText"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Shows the Add Fixture dialog; returns true when the user clicked Add.</summary>
    bool ShowAddFixture(ViewModels.AddFixtureViewModel viewModel);
}
