using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>Flash editor: fixtures, colour and duration (0 = while the button is held).</summary>
public sealed class FlashEditorViewModel : EffectEditorViewModel
{
    private double _durationSeconds;

    public FlashEditorViewModel(Project project, FlashFunction flash)
        : base(project, flash)
    {
        ColorEditor = new ColorEditorViewModel(_ => MarkDirty());
        LoadFromOriginal();
    }

    public ColorEditorViewModel ColorEditor { get; }

    public double? DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            SetNumber(ref _durationSeconds, value, 0, FunctionEditing.MaxSeconds);
            OnPropertyChanged(nameof(DurationHint));
        }
    }

    /// <summary>What the duration means right now.</summary>
    public string DurationHint => _durationSeconds <= 0
        ? "0 = on while held"
        : $"Off after {EditorTiming.FormatDuration(_durationSeconds)} (or on release)";

    private FlashFunction Flash => (FlashFunction)Original;

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
}
