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
    /// <summary>True while one of these prompts or another modal window is open (auto-save waits for it to close).</summary>
    bool IsModalOpen { get; }

    Task ShowErrorAsync(string title, string message);

    Task ShowInfoAsync(string title, string message);

    Task<SaveChangesChoice> AskSaveChangesAsync(string projectName);

    /// <summary>
    /// The open project's file was changed by another program while this app has unsaved changes: reload it, or keep
    /// the changes made here (the next save overwrites the file).
    /// </summary>
    Task<ExternalChangeChoice> AskReloadExternalChangeAsync(string fileName);

    /// <summary>Asks a yes/no question; returns true when the user picks <paramref name="confirmText"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Shows the Add Fixture dialog; returns true when the user clicked Add.</summary>
    bool ShowAddFixture(ViewModels.AddFixtureViewModel viewModel);

    /// <summary>Shows the channel layout editor; returns true when the layout was saved.</summary>
    bool ShowChannelLayout(ViewModels.ChannelLayoutViewModel viewModel);

    /// <summary>Shows the "Save as function" name dialog; returns true when the user clicked Save (name is valid).</summary>
    bool ShowSaveFunction(ViewModels.SaveFunctionDialogViewModel viewModel);
}
