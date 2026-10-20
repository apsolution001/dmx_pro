using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>A row of the Add Fixture profile tree: a manufacturer group or a fixture model.</summary>
public abstract partial class ProfileTreeNode : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded = true;

    public abstract string Name { get; }
}
