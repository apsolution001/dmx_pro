using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Pulse editor ("breathing"): lights, colour or "brightness only", speed (Slow / Medium / Fast or exact seconds per
/// breath), shape (smooth / straight) and the darkest / brightest level in percent, plus an on-screen preview. Raising
/// the darkest level above the brightest (or the other way round) moves the other level along.
/// </summary>
public sealed partial class PulseEditorViewModel : EffectEditorViewModel
{
    /// <summary>"Slow": one breath every 4 seconds.</summary>
    public const double SlowPeriodSeconds = 4;

    /// <summary>"Medium": one breath every 2 seconds (the default of a new pulse).</summary>
    public const double MediumPeriodSeconds = PulseFunction.DefaultPeriodSeconds;

    /// <summary>"Fast": one breath every second.</summary>
    public const double FastPeriodSeconds = 1;

    private double _periodSeconds;
    private double _minPercent;
    private double _maxPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesColor), nameof(PreviewColorHex))]
    private bool _dimmerOnly;

    [ObservableProperty]
    private PulseShape _shape;

    public PulseEditorViewModel(Project project, PulseFunction pulse)
        : base(project, pulse)
    {
        ColorEditor = new ColorEditorViewModel(_ => OnColorEdited()) { IsCustomOpen = false };
        SpeedPresets = new EffectChoiceGroup<double>(
            [
                (SlowPeriodSeconds, "Slow", "One breath every 4 seconds"),
                (MediumPeriodSeconds, "Medium", "One breath every 2 seconds"),
                (FastPeriodSeconds, "Fast", "One breath every second"),
            ],
            v => PeriodSeconds = v,
            EffectEditorFormat.NearlyEqual);
        ShapeChoices = new EffectChoiceGroup<PulseShape>(
            [
                (PulseShape.Sine, "Smooth", "Soft rise and fall, like breathing"),
                (PulseShape.Triangle, "Straight", "Rises and falls at an even pace, turning at the top and bottom"),
            ],
            v => Shape = v);
        LoadFromOriginal();
        SelectStartTab();
    }

    public ColorEditorViewModel ColorEditor { get; }

    /// <summary>The pulse sets a colour (the opposite of <see cref="DimmerOnly"/>); settable for a radio button.</summary>
    public bool UsesColor
    {
        get => !DimmerOnly;
        set => DimmerOnly = !value;
    }

    public IReadOnlyList<EditorOption<PulseShape>> Shapes { get; } =
    [
        new(PulseShape.Sine, "Sine (smooth breathing)", "Smooth rise and fall"),
        new(PulseShape.Triangle, "Triangle (linear)", "Linear up, linear down"),
    ];

    /// <summary>Slow / Medium / Fast: seconds per breath.</summary>
    public EffectChoiceGroup<double> SpeedPresets { get; }

    /// <summary>Smooth (sine) / Straight (triangle).</summary>
    public EffectChoiceGroup<PulseShape> ShapeChoices { get; }

    public double MinPeriodSeconds => PulseFunction.MinPeriodSeconds;

    /// <summary>Seconds of one full dark → bright → dark breath.</summary>
    public double? PeriodSeconds
    {
        get => _periodSeconds;
        set
        {
            SetNumber(ref _periodSeconds, value, PulseFunction.MinPeriodSeconds, FunctionEditing.MaxSeconds);
            SpeedPresets.Sync(_periodSeconds);
            OnPropertyChanged(nameof(SpeedText));
            RefreshPreview();
        }
    }

    /// <summary>"= 30 times a minute".</summary>
    public string SpeedText => "= " + EffectEditorFormat.TimesPerMinute(_periodSeconds);

    /// <summary>Lowest level, 0–100 %.</summary>
    public double? MinLevelPercent
    {
        get => _minPercent;
        set
        {
            SetNumber(ref _minPercent, value is { } v ? Math.Round(v) : null, 0, 100);
            if (_minPercent > _maxPercent)
            {
                SetNumber(ref _maxPercent, _minPercent, 0, 100, nameof(MaxLevelPercent));
            }

            RefreshPreview();
        }
    }

    /// <summary>Highest level, 0–100 %.</summary>
    public double? MaxLevelPercent
    {
        get => _maxPercent;
        set
        {
            SetNumber(ref _maxPercent, value is { } v ? Math.Round(v) : null, 0, 100);
            if (_maxPercent < _minPercent)
            {
                SetNumber(ref _minPercent, _maxPercent, 0, 100, nameof(MinLevelPercent));
            }

            RefreshPreview();
        }
    }

    /// <summary>On-screen preview: two breaths, dark → bright → dark.</summary>
    public IReadOnlyList<EffectPreviewPoint> PreviewPoints { get; private set; } = [];

    /// <summary>Seconds the preview window covers (two breaths), for its moving marker.</summary>
    public double PreviewWindowSeconds => _periodSeconds * EffectPreviewMath.PulseCyclesShown;

    /// <summary>The preview moves (breaths of half a second or longer); faster pulses are a still picture.</summary>
    public bool IsPreviewAnimated => EffectPreviewMath.CanAnimatePulse(_periodSeconds);

    /// <summary>Lamp colour of the preview (a real light colour); white for "brightness only".</summary>
    public string PreviewColorHex => DimmerOnly ? "#FFFFFF" : ColorEditor.SwatchHex;

    /// <summary>"One breath every 2 s, from 0 % to 100 % brightness."</summary>
    public string PreviewCaption
    {
        get
        {
            var text = $"One breath every {EffectEditorFormat.Seconds(_periodSeconds)}, from {_minPercent:0} % to {_maxPercent:0} % brightness.";
            if (_minPercent >= _maxPercent)
            {
                text += " Darkest and brightest are the same, so nothing moves.";
            }
            else if (!IsPreviewAnimated)
            {
                text += " Too fast to animate on screen; the lights still do it.";
            }

            return text;
        }
    }

    private PulseFunction Pulse => (PulseFunction)Target;

    protected override void LoadEffect()
    {
        DimmerOnly = Pulse.Color is null;
        ColorEditor.SetColorSilently(Pulse.Color ?? new RgbColor(255, 255, 255));
        Shape = Pulse.Shape;
        ShapeChoices.Sync(Shape);
        PeriodSeconds = Pulse.PeriodSeconds;
        _minPercent = 0;
        _maxPercent = 100;
        MaxLevelPercent = Pulse.MaxLevel * 100;
        MinLevelPercent = Pulse.MinLevel * 100;
        OnPropertyChanged(nameof(PreviewColorHex));
    }

    protected override void ApplyEffect()
    {
        Pulse.Color = DimmerOnly ? null : ColorEditor.Color;
        Pulse.Shape = Shape;
        Pulse.PeriodSeconds = _periodSeconds;
        Pulse.MinLevel = _minPercent / 100.0;
        Pulse.MaxLevel = _maxPercent / 100.0;
    }

    partial void OnDimmerOnlyChanged(bool value) => MarkDirty();

    partial void OnShapeChanged(PulseShape value)
    {
        ShapeChoices.Sync(value);
        RefreshPreview();
        MarkDirty();
    }

    private void OnColorEdited()
    {
        OnPropertyChanged(nameof(PreviewColorHex));
        MarkDirty();
    }

    private void RefreshPreview()
    {
        // Shape is set before the levels while loading; the preview is complete once both are in.
        PreviewPoints = EffectPreviewMath.PulseCurve(Shape, _minPercent / 100.0, _maxPercent / 100.0);
        OnPropertyChanged(nameof(PreviewPoints));
        OnPropertyChanged(nameof(PreviewWindowSeconds));
        OnPropertyChanged(nameof(IsPreviewAnimated));
        OnPropertyChanged(nameof(PreviewCaption));
    }
}
