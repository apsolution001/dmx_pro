using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One button of a pick-one row in the effect editors (speed Slow / Medium / Fast, pulse shape …), bound to a radio
/// button: checking it applies <see cref="Value"/> through its <see cref="EffectChoiceGroup{T}"/>. Which button is
/// checked always follows the editor's real value, so typing an exact value that matches no preset unchecks them all.
/// </summary>
/// <typeparam name="T">The value type (seconds, Hz, an enum …).</typeparam>
public sealed class EffectChoice<T> : ObservableObject
{
    private readonly Action<EffectChoice<T>> _choose;
    private bool _isSelected;

    internal EffectChoice(T value, string label, string toolTip, Action<EffectChoice<T>> choose)
    {
        Value = value;
        Label = label;
        ToolTip = toolTip;
        _choose = choose;
    }

    public T Value { get; }

    public string Label { get; }

    public string ToolTip { get; }

    /// <summary>Checked: this is the current value. Setting true applies it; setting false is ignored (pick another one).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
            {
                _choose(this);
            }

            // Re-raise so a radio button that was (un)checked by the user shows the real state.
            OnPropertyChanged();
        }
    }

    internal void SetSelected(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
}
