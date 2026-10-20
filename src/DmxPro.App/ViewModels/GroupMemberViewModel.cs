using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Patching;

namespace DmxPro.App.ViewModels;

/// <summary>A member fixture under a group node in the Groups tree (name + universe/address).</summary>
public sealed partial class GroupMemberViewModel : ObservableObject
{
    private readonly Action<GroupMemberViewModel> _onSelected;

    [ObservableProperty]
    private bool _isSelected;

    public GroupMemberViewModel(GroupRowViewModel parent, Fixture fixture, int position, string addressText, Action<GroupMemberViewModel> onSelected)
    {
        Parent = parent;
        Fixture = fixture;
        Position = position;
        AddressText = addressText;
        _onSelected = onSelected;
    }

    public GroupRowViewModel Parent { get; }

    public Fixture Fixture { get; }

    /// <summary>1-based position in the group (the order chasers and effects will step through).</summary>
    public int Position { get; }

    public string Name => Fixture.Name;

    /// <summary>e.g. "U1 · 1–8".</summary>
    public string AddressText { get; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _onSelected(this);
        }
    }
}
