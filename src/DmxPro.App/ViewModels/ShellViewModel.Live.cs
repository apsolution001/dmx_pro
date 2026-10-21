using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Main toolbar "Go live" toggle (owner decision 2026-10-22): a mode switch like QLC+ Operate mode. While live only the
/// Virtual Console drives the lights; it is not output enable/disable (that stays on Ctrl+E / the status pill).
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Keyboard shortcut of <see cref="ToggleLiveModeCommand"/> (F5 is taken by the Input / Output page refresh
    /// and can be a Virtual Console key).</summary>
    public const string LiveModeShortcut = "Ctrl+F5";

    private ILiveModeService? _liveMode;

    /// <summary>Live mode on/off; bound two-way to the toolbar toggle. Always off at start and on project open.</summary>
    public bool IsLive
    {
        get => _liveMode?.IsLive ?? false;
        set
        {
            if (_liveMode is { } liveMode && liveMode.IsLive != value)
            {
                liveMode.SetLive(value);
            }

            // Re-raised even when nothing changed, so the toggle snaps back to the real state.
            OnPropertyChanged();
        }
    }

    /// <summary>"Go live" (off) / "Live" (on): the text changes too, so the state is not shown by colour only.</summary>
    public string LiveButtonText => IsLive ? "Live" : "Go live";

    /// <summary>▶ while off (press to go live), ⏸ while live (press to stop).</summary>
    public string LiveIconKind => IsLive ? "Pause" : "Play";

    public string LiveToolTip => IsLive
        ? $"Live: only the Virtual Console drives the lights. Editor previews, test values and Shows-page playback are off. Click to leave live mode ({LiveModeShortcut})."
        : $"Go live: only the Virtual Console will drive the lights (editor previews, test values and Shows-page playback stop). Output on/off is separate ({LiveModeShortcut}).";

    public string LiveAutomationName => IsLive ? "Live mode on. Leave live mode" : "Go live";

    [RelayCommand]
    private void ToggleLiveMode() => IsLive = !IsLive;

    private void InitializeLiveMode(ILiveModeService liveMode)
    {
        _liveMode = liveMode;
        liveMode.LiveModeChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(LiveButtonText));
            OnPropertyChanged(nameof(LiveIconKind));
            OnPropertyChanged(nameof(LiveToolTip));
            OnPropertyChanged(nameof(LiveAutomationName));
        };
    }
}
