using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One fixture group in the "Choose lights" dialog. Its tick follows the picked lights: ticked when every member is
/// picked, indeterminate when some are. Ticking picks every member; unticking unpicks every member.
/// </summary>
public sealed class FixturePickerGroup : ObservableObject
{
    private readonly Action<FixturePickerGroup, bool> _onToggled;
    private bool? _isChecked = false;

    public FixturePickerGroup(Guid id, string name, IReadOnlyList<Guid> memberIds, Action<FixturePickerGroup, bool> onToggled)
    {
        Id = id;
        Name = name;
        MemberIds = memberIds;
        _onToggled = onToggled;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>The group's patched lights.</summary>
    public IReadOnlyList<Guid> MemberIds { get; }

    /// <summary>False for a group without patched lights (nothing to tick).</summary>
    public bool HasMembers => MemberIds.Count > 0;

    /// <summary>"4 lights".</summary>
    public string CountText => MemberIds.Count == 1 ? "1 light" : $"{MemberIds.Count} lights";

    public string AutomationName => $"Group {Name}, {CountText}";

    /// <summary>True = all members picked, false = none, null = some. Setting it (the user) picks or unpicks every member.</summary>
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            OnPropertyChanged();

            // A two-state box goes from indeterminate to ticked; treat anything but "ticked" as "unpick all".
            _onToggled(this, value == true);
        }
    }

    /// <summary>Updates the tick from the picked lights without picking or unpicking anything.</summary>
    internal void SetStateSilently(bool? value) => SetProperty(ref _isChecked, value, nameof(IsChecked));
}
