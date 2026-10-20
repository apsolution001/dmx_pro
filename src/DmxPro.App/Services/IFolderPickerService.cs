namespace DmxPro.App.Services;

/// <summary>Folder picker, behind an interface so ViewModels stay testable.</summary>
public interface IFolderPickerService
{
    /// <summary>Returns the chosen folder, or null when the user cancels.</summary>
    string? PickFolder(string title, string? initialDirectory);
}
