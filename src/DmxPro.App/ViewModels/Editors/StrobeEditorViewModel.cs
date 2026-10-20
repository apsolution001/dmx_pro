using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Strobe editor: mode (software / the fixture's own strobe channel), rate, duty cycle, colour and the raw hardware
/// value. Safety rule 8: shows the global "allow strobe" state and the maximum rate from Settings (live, via
/// <see cref="AppSettingsService.Changed"/>) — the engine enforces both; this editor only explains them.
/// </summary>
public sealed partial class StrobeEditorViewModel : EffectEditorViewModel
{
    /// <summary>Highest rate the editor accepts; the Settings cap (≤ 25 Hz) applies on top when playing.</summary>
    public const double MaxRateHz = 50;

    /// <summary>Slowest software strobe the editor accepts.</summary>
    public const double MinRateHz = 0.1;

    private readonly AppSettingsService _settings;
    private double _rateHz;
    private double _dutyPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHardwareMode), nameof(ModeHint))]
    private StrobeMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesCustomHardwareValue))]
    private bool _useProfileHardwareValue;

    [ObservableProperty]
    private int _hardwareValue;

    public StrobeEditorViewModel(Project project, StrobeFunction strobe, AppSettingsService settings)
        : base(project, strobe)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        ColorEditor = new ColorEditorViewModel(_ => MarkDirty());
        LoadFromOriginal();
    }

    public ColorEditorViewModel ColorEditor { get; }

    public IReadOnlyList<EditorOption<StrobeMode>> Modes { get; } =
    [
        new(StrobeMode.Software, "Software (DMX Pro flashes the light)", "Works on every fixture; the rate is exact and capped by the maximum strobe rate"),
        new(StrobeMode.Hardware, "Fixture strobe channel", "Uses each fixture's own Strobe channel; fixtures without one use software strobe"),
    ];

    public bool IsHardwareMode => Mode == StrobeMode.Hardware;

    public bool UsesCustomHardwareValue => !UseProfileHardwareValue;

    public string ModeHint => Mode == StrobeMode.Hardware
        ? "The fixture decides the real speed from the value below (see its DMX chart). Fixtures without a Strobe channel use software strobe at the rate above."
        : "DMX Pro switches the colour on and off at the rate above.";

    /// <summary>Flashes per second.</summary>
    public double? RateHz
    {
        get => _rateHz;
        set
        {
            SetNumber(ref _rateHz, value, MinRateHz, MaxRateHz);
            OnPropertyChanged(nameof(IsRateCapped));
            OnPropertyChanged(nameof(RateCapText));
        }
    }

    /// <summary>Part of each flash the light is on, 5–95 %.</summary>
    public double? DutyPercent
    {
        get => _dutyPercent;
        set => SetNumber(ref _dutyPercent, value is { } v ? Math.Round(v) : null, StrobeFunction.MinDutyCycle * 100, StrobeFunction.MaxDutyCycle * 100);
    }

    public double MinDutyPercent => StrobeFunction.MinDutyCycle * 100;

    public double MaxDutyPercent => StrobeFunction.MaxDutyCycle * 100;

    /// <summary>Settings → "Allow strobe". Off: every strobe lights steady instead.</summary>
    public bool IsStrobeAllowed => _settings.Current.AllowStrobe;

    public bool IsStrobeDisabled => !IsStrobeAllowed;

    public int MaxStrobeRateHz => _settings.Current.MaxStrobeRateHz;

    /// <summary>"Max 10 Hz (Settings)".</summary>
    public string MaxRateText => $"Max {MaxStrobeRateHz} Hz (Settings)";

    /// <summary>The rate is faster than the Settings cap: the engine slows it down.</summary>
    public bool IsRateCapped => _rateHz > MaxStrobeRateHz;

    public string RateCapText =>
        $"Capped: flashes at {MaxStrobeRateHz} Hz, not {_rateHz.ToString("0.##", CultureInfo.CurrentCulture)} Hz (Settings maximum)";

    private StrobeFunction Strobe => (StrobeFunction)Original;

    public override void Detach() => _settings.Changed -= OnSettingsChanged;

    protected override void LoadEffect()
    {
        Mode = Strobe.Mode;
        RateHz = Strobe.RateHz;
        DutyPercent = Strobe.DutyCycle * 100;
        ColorEditor.SetColorSilently(Strobe.Color);
        UseProfileHardwareValue = Strobe.HardwareValue is null;
        HardwareValue = Strobe.HardwareValue ?? 0;
    }

    protected override void ApplyEffect()
    {
        Strobe.Mode = Mode;
        Strobe.RateHz = _rateHz;
        Strobe.DutyCycle = _dutyPercent / 100.0;
        Strobe.Color = ColorEditor.Color;
        Strobe.HardwareValue = UseProfileHardwareValue ? null : (byte)Math.Clamp(HardwareValue, 0, 255);
    }

    partial void OnModeChanged(StrobeMode value) => MarkDirty();

    partial void OnUseProfileHardwareValueChanged(bool value) => MarkDirty();

    partial void OnHardwareValueChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 255);
        if (clamped != value)
        {
            HardwareValue = clamped;
            return;
        }

        MarkDirty();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsStrobeAllowed));
        OnPropertyChanged(nameof(IsStrobeDisabled));
        OnPropertyChanged(nameof(MaxStrobeRateHz));
        OnPropertyChanged(nameof(MaxRateText));
        OnPropertyChanged(nameof(IsRateCapped));
        OnPropertyChanged(nameof(RateCapText));
    }
}
