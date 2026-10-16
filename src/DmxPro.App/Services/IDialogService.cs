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
}
