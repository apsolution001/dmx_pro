using System.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Flash editor: lights, colour and how long it stays on — "while the button is held" (duration 0) or "for a set
/// time" (goes off by itself after that time, or sooner on release). The last set time is remembered while "held" is
/// picked, so switching back and forth keeps it.
/// </summary>
public sealed class FlashEditorViewModel : EffectEditorViewModel
{
    /// <summary>Time offered when "for a set time" is picked for the first time.</summary>
    public const double DefaultTimedSeconds = 1;

    /// <summary>Shortest set time (0 would mean "while held").</summary>
    public const double MinTimedSeconds = 0.1;

    private double _durationSeconds;
    private double _lastTimedSeconds = DefaultTimedSeconds;

    public FlashEditorViewModel(Project project, FlashFunction flash)
        : base(project, flash)
    {
        ColorEditor = new ColorEditorViewModel(_ => MarkDirty()) { IsCustomOpen = false };
        ColorEditor.PropertyChanged += OnColorEditorPropertyChanged;
        LoadFromOriginal();
    }

    public ColorEditorViewModel ColorEditor { get; }

    /// <summary>Auto-off time in seconds; 0 = while the button is held.</summary>
    public double? DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            SetNumber(ref _durationSeconds, value, 0, FunctionEditing.MaxSeconds);
            if (_durationSeconds > 0)
            {
                _lastTimedSeconds = _durationSeconds;
            }

            OnPropertyChanged(nameof(DurationHint));
            OnPropertyChanged(nameof(IsHeld));
            OnPropertyChanged(nameof(IsTimed));
            OnPropertyChanged(nameof(TimedSeconds));
            OnPropertyChanged(nameof(PreviewCaption));
        }
    }

    /// <summary>"While the button is held" is picked (duration 0). Settable for a radio button.</summary>
    public bool IsHeld
    {
        get => _durationSeconds <= 0;
        set
        {
            if (value && _durationSeconds > 0)
            {
                DurationSeconds = 0;
            }
            else if (!value && _durationSeconds <= 0)
            {
                DurationSeconds = _lastTimedSeconds;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>"For a set time" is picked. Settable for a radio button.</summary>
    public bool IsTimed
    {
        get => !IsHeld;
        set => IsHeld = !value;
    }

    /// <summary>
    /// The set time in seconds (at least <see cref="MinTimedSeconds"/>). While "held" is picked it shows (and changes)
    /// the remembered time without using it.
    /// </summary>
    public double? TimedSeconds
    {
        get => _durationSeconds > 0 ? _durationSeconds : _lastTimedSeconds;
        set
        {
            if (value is not { } v || !double.IsFinite(v))
            {
                OnPropertyChanged();
                return;
            }

            var seconds = FunctionEditing.Clamp(v, MinTimedSeconds, FunctionEditing.MaxSeconds);
            if (_durationSeconds > 0)
            {
                DurationSeconds = seconds;
            }
            else
            {
                _lastTimedSeconds = seconds;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>What the duration means right now.</summary>
    public string DurationHint => _durationSeconds <= 0
        ? "0 = on while held"
        : $"Off after {EditorTiming.FormatDuration(_durationSeconds)} (or on release)";

    /// <summary>What happens when the button is pressed, in plain words.</summary>
    public string PreviewCaption => _durationSeconds <= 0
        ? $"Press and hold: the lights jump to full {ColorEditor.ColorNameInSentence}. Let go: they go straight back."
        : $"Press: the lights jump to full {ColorEditor.ColorNameInSentence} for {EditorTiming.FormatDuration(_durationSeconds)}, then go back by themselves (or sooner if you let go).";

    private FlashFunction Flash => (FlashFunction)Target;

    protected override void LoadEffect()
    {
        ColorEditor.SetColorSilently(Flash.Color);
        DurationSeconds = Flash.DurationSeconds;
    }

    protected override void ApplyEffect()
    {
        Flash.Color = ColorEditor.Color;
        Flash.DurationSeconds = _durationSeconds;
    }

    private void OnColorEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ColorEditorViewModel.ColorNameInSentence))
        {
            OnPropertyChanged(nameof(PreviewCaption));
        }
    }
}
