using CommunityToolkit.Mvvm.ComponentModel;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>A quick duration button of the fade editor ("3 s"); <see cref="IsActive"/> when it matches the duration.</summary>
public sealed partial class FadeDurationPreset : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public FadeDurationPreset(double seconds)
    {
        Seconds = seconds;
        Label = EditorTiming.FormatDuration(seconds);
    }

    public double Seconds { get; }

    /// <summary>"3 s".</summary>
    public string Label { get; }

    public string AutomationName => $"Duration {Label}";
}
