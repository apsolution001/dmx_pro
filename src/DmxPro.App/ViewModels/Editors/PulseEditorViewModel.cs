using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Pulse editor: fixtures, colour or "dimmer only", shape, period and the minimum / maximum level in percent. Raising
/// the minimum above the maximum (or the other way round) moves the other level along.
/// </summary>
public sealed partial class PulseEditorViewModel : EffectEditorViewModel
{
    private double _periodSeconds;
    private double _minPercent;
    private double _maxPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesColor))]
    private bool _dimmerOnly;

    [ObservableProperty]
    private PulseShape _shape;

    public PulseEditorViewModel(Project project, PulseFunction pulse)
        : base(project, pulse)
    {
        ColorEditor = new ColorEditorViewModel(_ => MarkDirty());
        LoadFromOriginal();
    }

    public ColorEditorViewModel ColorEditor { get; }

    public bool UsesColor => !DimmerOnly;

    public IReadOnlyList<EditorOption<PulseShape>> Shapes { get; } =
    [
        new(PulseShape.Sine, "Sine (smooth breathing)", "Smooth rise and fall"),
        new(PulseShape.Triangle, "Triangle (linear)", "Linear up, linear down"),
    ];

    public double MinPeriodSeconds => PulseFunction.MinPeriodSeconds;

    public double? PeriodSeconds
    {
        get => _periodSeconds;
        set => SetNumber(ref _periodSeconds, value, PulseFunction.MinPeriodSeconds, FunctionEditing.MaxSeconds);
    }

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
        }
    }

    private PulseFunction Pulse => (PulseFunction)Original;

    protected override void LoadEffect()
    {
        DimmerOnly = Pulse.Color is null;
        ColorEditor.SetColorSilently(Pulse.Color ?? new RgbColor(255, 255, 255));
        Shape = Pulse.Shape;
        PeriodSeconds = Pulse.PeriodSeconds;
        _minPercent = 0;
        _maxPercent = 100;
        MaxLevelPercent = Pulse.MaxLevel * 100;
        MinLevelPercent = Pulse.MinLevel * 100;
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

    partial void OnShapeChanged(PulseShape value) => MarkDirty();
}
