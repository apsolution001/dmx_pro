namespace DmxPro.App.Services;

/// <summary>Open/save file pickers, behind an interface so ViewModels stay testable.</summary>
public interface IFileDialogService
{
    string? ShowOpenProject(string initialDirectory);

    string? ShowSaveProject(string initialDirectory, string suggestedName);

    IReadOnlyList<string> ShowOpenProfiles();
}
