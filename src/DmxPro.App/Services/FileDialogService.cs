using DmxPro.Audio;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using Microsoft.Win32;

namespace DmxPro.App.Services;

public sealed class FileDialogService : IFileDialogService
{
    private const string ProjectFilter = $"DMX Pro project (*{Project.FileExtension})|*{Project.FileExtension}";

    public string? ShowOpenProject(string initialDirectory)
    {
        var dialog = new OpenFileDialog { Filter = ProjectFilter, InitialDirectory = initialDirectory };
        return dialog.ShowDialog(DialogOwner.ActiveOwner()) == true ? dialog.FileName : null;
    }

    public string? ShowSaveProject(string initialDirectory, string suggestedName)
    {
        Directory.CreateDirectory(initialDirectory);
        var dialog = new SaveFileDialog
        {
            Filter = ProjectFilter,
            InitialDirectory = initialDirectory,
            FileName = suggestedName + Project.FileExtension,
            AddExtension = true,
        };
        return dialog.ShowDialog(DialogOwner.ActiveOwner()) == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> ShowOpenProfiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"Fixture profiles (*{QxfImporter.FileExtension};*{ProfileSerializer.FileExtension})|*{QxfImporter.FileExtension};*.json|" +
                     $"QLC+ fixture (*{QxfImporter.FileExtension})|*{QxfImporter.FileExtension}|" +
                     $"DMX Pro profile (*{ProfileSerializer.FileExtension})|*.json",
            Multiselect = true,
        };
        return dialog.ShowDialog(DialogOwner.ActiveOwner()) == true ? dialog.FileNames : [];
    }

    public string? ShowOpenAudio(string? initialDirectory)
    {
        var dialog = new OpenFileDialog { Filter = AudioFormats.DialogFilter, Title = "Choose music" };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog(DialogOwner.ActiveOwner()) == true ? dialog.FileName : null;
    }
}
