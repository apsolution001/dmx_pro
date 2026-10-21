using System.Windows.Input;

namespace DmxPro.App.ViewModels;

/// <summary>One entry of File ▸ Open recent. Carries its command so the generated submenu item needs no ancestor lookup.</summary>
public sealed class RecentProjectItem
{
    public RecentProjectItem(int number, string path, ICommand openCommand)
    {
        Path = path;
        OpenCommand = openCommand;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);

        // "_1 Wedding": the number is the access key; underscores in the name must not become access keys.
        Header = $"_{number} {name.Replace("_", "__", StringComparison.Ordinal)}";
    }

    /// <summary>Full path of the project file (also the tooltip and the command parameter).</summary>
    public string Path { get; }

    public string Header { get; }

    /// <summary>Opens <see cref="Path"/> (the shell's Open recent command).</summary>
    public ICommand OpenCommand { get; }
}
