using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels;

/// <summary>
/// A type folder of the Functions tree ("Scene ▸ arjun, blue, …"), like QLC+'s Function Manager. Selecting the folder
/// itself clears the function selection (<paramref name="onSelected"/>).
/// </summary>
public sealed partial class FunctionTreeGroupViewModel : ObservableObject
{
    private readonly Action<FunctionTreeGroupViewModel>? _onSelected;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public FunctionTreeGroupViewModel(FunctionType type, IReadOnlyList<FunctionRowViewModel> children, Action<FunctionTreeGroupViewModel>? onSelected = null)
    {
        Type = type;
        Children = children;
        _onSelected = onSelected;
    }

    public FunctionType Type { get; }

    public string Header => FunctionRowViewModel.TypeDisplayName(Type);

    /// <summary>Icon kind (<c>DmxIcon.Kind</c>) for the function type.</summary>
    public string IconKind => FunctionRowViewModel.TypeIconKind(Type);

    public IReadOnlyList<FunctionRowViewModel> Children { get; }

    public int Count => Children.Count;

    public string AutomationName => $"{Header}, {Count} {(Count == 1 ? "function" : "functions")}";

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _onSelected?.Invoke(this);
        }
    }
}
