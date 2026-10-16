using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Patching;

namespace DmxPro.App.ViewModels;

/// <summary>A fixture group in the Groups panel. The name is edited in place.</summary>
public sealed partial class GroupRowViewModel : ObservableObject
{
    private readonly Action _onEdited;

    public GroupRowViewModel(FixtureGroup group, string members, Action onEdited)
    {
        Group = group;
        Members = members;
        _onEdited = onEdited;
    }

    public FixtureGroup Group { get; }

    public string Members { get; }

    public string Name
    {
        get => Group.Name;
        set
        {
            if (Group.Name != value && !string.IsNullOrWhiteSpace(value))
            {
                Group.Name = value.Trim();
                OnPropertyChanged();
                _onEdited();
            }
        }
    }
}
