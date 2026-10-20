using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.Core.Patching;

namespace DmxPro.App.ViewModels;

/// <summary>
/// A fixture group node in the Groups tree. Its children are the member fixtures, in group order.
/// The name is renamed in place (F2): <see cref="IsEditing"/> swaps the label for a text box bound to <see cref="EditName"/>.
/// </summary>
public sealed partial class GroupRowViewModel : ObservableObject
{
    private readonly Action<GroupRowViewModel> _onSelected;
    private readonly Func<GroupRowViewModel, string, bool> _rename;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    public GroupRowViewModel(
        FixtureGroup group,
        Action<GroupRowViewModel> onSelected,
        Func<GroupRowViewModel, string, bool> rename)
    {
        Group = group;
        _onSelected = onSelected;
        _rename = rename;
    }

    public FixtureGroup Group { get; }

    public string Name => Group.Name;

    public bool IsNotEditing => !IsEditing;

    public ObservableCollection<GroupMemberViewModel> Members { get; } = [];

    public string MemberCountText => Members.Count == 1 ? "1 fixture" : $"{Members.Count} fixtures";

    /// <summary>Re-reads the name and member count after the model changed.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(MemberCountText));
    }

    /// <summary>Starts inline rename with the current name.</summary>
    public void BeginRename()
    {
        if (IsEditing)
        {
            return;
        }

        EditName = Group.Name;
        IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        if (EditName.Trim() != Group.Name && _rename(this, EditName))
        {
            Refresh();
        }
    }

    [RelayCommand]
    private void CancelRename() => IsEditing = false;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _onSelected(this);
        }
    }
}
