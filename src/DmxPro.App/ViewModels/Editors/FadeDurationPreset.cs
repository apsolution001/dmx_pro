using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// A quick duration button of the fade editor ("3 s"), bound to a radio button. <see cref="IsActive"/> when it matches
/// the duration; checking it (<see cref="IsSelected"/>) applies its seconds through the editor.
/// </summary>
public sealed partial class FadeDurationPreset : ObservableObject
{
    private readonly Action<FadeDurationPreset>? _choose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelected))]
    private bool _isActive;

    public FadeDurationPreset(double seconds, Action<FadeDurationPreset>? choose = null)
    {
        Seconds = seconds;
        Label = EditorTiming.FormatDuration(seconds);
        _choose = choose;
    }

    public double Seconds { get; }

    /// <summary>"3 s".</summary>
    public string Label { get; }

    public string AutomationName => $"Duration {Label}";

    /// <summary>
    /// Radio button state: the same as <see cref="IsActive"/>. Setting true applies this duration; setting false is
    /// ignored (pick another preset or type a value), so the checked button always follows the real duration.
    /// </summary>
    public bool IsSelected
    {
        get => IsActive;
        set
        {
            if (value && !IsActive)
            {
                _choose?.Invoke(this);
            }

            // Re-raise so a radio button the user (un)checked shows the real state.
            OnPropertyChanged();
        }
    }
}
