using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>A tab of the scene editor (General, Channels).</summary>
public abstract partial class SceneEditorTab : ObservableObject
{
    /// <summary>Text on the tab.</summary>
    public abstract string Header { get; }

    /// <summary><see cref="Controls.DmxIcon"/> kind shown next to the header.</summary>
    public abstract string IconKind { get; }
}
