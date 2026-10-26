using Microsoft.Win32;

namespace DmxPro.App.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title, string? initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog(DialogOwner.ActiveOwner()) == true ? dialog.FolderName : null;
    }
}
