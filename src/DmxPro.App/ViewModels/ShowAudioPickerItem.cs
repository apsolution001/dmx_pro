using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Shows;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One entry of the Shows page show picker: the show plus its music status, so a missing or broken song is visible in
/// the list too (safety rule 10), not only on the timeline of the selected show.
/// </summary>
public sealed partial class ShowAudioPickerItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip), nameof(HasMusicProblem))]
    private string? _musicProblem;

    public ShowAudioPickerItem(Show show)
    {
        Show = show;
    }

    public Show Show { get; }

    public string Name => Show.Name;

    public bool HasMusicProblem => MusicProblem is not null;

    public bool HasMusic => Show.Audio is not null;

    public string ToolTip => MusicProblem is { } problem ? $"{Name}\n{problem}" : Name;
}
