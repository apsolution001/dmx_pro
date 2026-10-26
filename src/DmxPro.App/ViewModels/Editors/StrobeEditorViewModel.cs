using CommunityToolkit.Mvvm.ComponentModel;
using DmxPro.App.Services;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Strobe editor: lights, colour, how the flashing is made (DMX Pro flashes the lights, or each light's own strobe
/// channel), speed (Slow / Medium / Fast or exact flashes per second), flash length (Short / Normal / Long or exact %)
/// and the raw strobe channel value for the light's own strobe. Safety rule 8: shows the global "allow strobe" state
/// and the maximum rate from Settings (live, via <see cref="AppSettingsService.Changed"/>), what the lights will really
/// do, and that the maximum cannot be checked for a light's own strobe. The engine enforces both settings; this editor
/// only explains them. The on-screen preview is a still picture on purpose (no flashing on screen).
/// </summary>
public sealed partial class StrobeEditorViewModel : EffectEditorViewModel
{
    /// <summary>Highest rate the editor accepts; the Settings cap (≤ 25 Hz) applies on top when playing.</summary>
    public const double MaxRateHz = 50;

    /// <summary>Slowest software strobe the editor accepts.</summary>
    public const double MinRateHz = 0.1;

    /// <summary>"Slow": 2 flashes per second.</summary>
    public const double SlowRateHz = 2;

    /// <summary>"Medium": 5 flashes per second (the default of a new strobe).</summary>
    public const double MediumRateHz = StrobeFunction.DefaultRateHz;

    /// <summary>"Fast": 10 flashes per second (the default Settings maximum).</summary>
    public const double FastRateHz = 10;

    private readonly AppSettingsService _settings;
    private double _rateHz;
    private double _dutyPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHardwareMode), nameof(IsSoftwareMode), nameof(ModeHint), nameof(PreviewCaption))]
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
        ColorEditor = new ColorEditorViewModel(_ => OnColorEdited()) { IsCustomOpen = false };
        SpeedPresets = new EffectChoiceGroup<double>(
            [
                (SlowRateHz, "Slow", "2 flashes per second"),
                (MediumRateHz, "Medium", "5 flashes per second"),
                (FastRateHz, "Fast", "10 flashes per second"),
            ],
            v => RateHz = v,
            EffectEditorFormat.NearlyEqual);
        LengthPresets = new EffectChoiceGroup<double>(
            [
                (20, "Short", "Each flash is on for a short moment (20 % of the time): a sharp, crisp strobe"),
                (50, "Normal", "On half the time, off half the time"),
                (80, "Long", "Each flash stays on most of the time (80 %): a softer flicker"),
            ],
            v => DutyPercent = v,
            EffectEditorFormat.NearlyEqual);
        LoadFromOriginal();
        SelectStartTab();
    }

    public ColorEditorViewModel ColorEditor { get; }

    public IReadOnlyList<EditorOption<StrobeMode>> Modes { get; } =
    [
        new(StrobeMode.Software, "Software (DMX Pro flashes the light)", "Works on every fixture; the rate is exact and capped by the maximum strobe rate"),
        new(StrobeMode.Hardware, "Fixture strobe channel", "Uses each fixture's own Strobe channel; fixtures without one use software strobe"),
    ];

    /// <summary>Slow / Medium / Fast: flashes per second.</summary>
    public EffectChoiceGroup<double> SpeedPresets { get; }

    /// <summary>Short / Normal / Long: part of each flash the light is on, in percent.</summary>
    public EffectChoiceGroup<double> LengthPresets { get; }

    /// <summary>Each light's own strobe channel is used. Settable for a radio button.</summary>
    public bool IsHardwareMode
    {
        get => Mode == StrobeMode.Hardware;
        set
        {
            if (value)
            {
                Mode = StrobeMode.Hardware;
            }
        }
    }

    /// <summary>DMX Pro switches the lights on and off. Settable for a radio button.</summary>
    public bool IsSoftwareMode
    {
        get => Mode == StrobeMode.Software;
        set
        {
            if (value)
            {
                Mode = StrobeMode.Software;
            }
        }
    }

    /// <summary>A raw strobe channel value is typed (not the profile's). Settable for a radio button.</summary>
    public bool UsesCustomHardwareValue
    {
        get => !UseProfileHardwareValue;
        set => UseProfileHardwareValue = !value;
    }

    public string ModeHint => Mode == StrobeMode.Hardware
        ? "Each light flashes by itself, at the speed its strobe channel value gives (see the light's manual). Lights without a strobe channel are flashed by DMX Pro at the speed above."
        : "DMX Pro switches the lights on and off at the speed above. Works on every light.";

    /// <summary>Flashes per second.</summary>
    public double? RateHz
    {
        get => _rateHz;
        set
        {
            SetNumber(ref _rateHz, value, MinRateHz, MaxRateHz);
            SpeedPresets.Sync(_rateHz);
            OnRateOrSettingsChanged();
        }
    }

    /// <summary>Part of each flash the light is on, 5–95 %.</summary>
    public double? DutyPercent
    {
        get => _dutyPercent;
        set
        {
            SetNumber(ref _dutyPercent, value is { } v ? Math.Round(v) : null, StrobeFunction.MinDutyCycle * 100, StrobeFunction.MaxDutyCycle * 100);
            LengthPresets.Sync(_dutyPercent);
            RefreshPreview();
        }
    }

    public double MinDutyPercent => StrobeFunction.MinDutyCycle * 100;

    public double MaxDutyPercent => StrobeFunction.MaxDutyCycle * 100;

    /// <summary>Settings → "Allow strobe". Off: every strobe lights steady instead.</summary>
    public bool IsStrobeAllowed => _settings.Current.AllowStrobe;

    public bool IsStrobeDisabled => !IsStrobeAllowed;

    public int MaxStrobeRateHz => _settings.Current.MaxStrobeRateHz;

    /// <summary>"Settings limit: 10 per second".</summary>
    public string MaxRateText => $"Settings limit: {MaxStrobeRateHz} per second";

    /// <summary>The rate is faster than the Settings cap: the engine slows it down.</summary>
    public bool IsRateCapped => _rateHz > MaxStrobeRateHz;

    /// <summary>The rate is capped and strobe is allowed: the lights really flash slower than asked.</summary>
    public bool ShowRateCap => IsRateCapped && IsStrobeAllowed;

    public string RateCapText =>
        $"Slowed down: Settings allow at most {EffectEditorFormat.FlashesPerSecond(MaxStrobeRateHz)}, so this flashes at {MaxStrobeRateHz}, not {EffectEditorFormat.Number(_rateHz)}.";

    /// <summary>Flashes per second the lights really make (0 = steady light, strobe not allowed).</summary>
    public double EffectiveRateHz => EffectPreviewMath.EffectiveStrobeRate(_rateHz, IsStrobeAllowed, MaxStrobeRateHz);

    /// <summary>"= 5 flashes per second".</summary>
    public string SpeedText => "= " + EffectEditorFormat.FlashesPerSecond(_rateHz);

    /// <summary>The light's own strobe: the Settings maximum cannot be checked (the light decides its speed).</summary>
    public string HardwareLimitText =>
        $"The Settings limit ({MaxStrobeRateHz} per second) cannot be checked for a light's own strobe: the light decides its speed. Pick a slow value from its manual.";

    /// <summary>Still picture of one second of strobe, as the lights will really do it.</summary>
    public IReadOnlyList<EffectPreviewPoint> PreviewPoints { get; private set; } = [];

    /// <summary>Lamp colour of the preview (a real light colour).</summary>
    public string PreviewColorHex => ColorEditor.SwatchHex;

    /// <summary>"One second: 5 flashes, each on for 50 % of the time." / "Steady light: strobe is off in Settings."</summary>
    public string PreviewCaption
    {
        get
        {
            if (!IsStrobeAllowed)
            {
                return "Steady light, no flashing: strobe is turned off in Settings.";
            }

            var text = $"One second: {EffectEditorFormat.FlashesPerSecond(EffectiveRateHz)}, each on for {_dutyPercent:0} % of the time.";
            return Mode == StrobeMode.Hardware
                ? text + " (Lights without their own strobe channel.)"
                : text;
        }
    }

    private StrobeFunction Strobe => (StrobeFunction)Target;

    public override void Detach() => _settings.Changed -= OnSettingsChanged;

    protected override void LoadEffect()
    {
        Mode = Strobe.Mode;
        RateHz = Strobe.RateHz;
        DutyPercent = Strobe.DutyCycle * 100;
        ColorEditor.SetColorSilently(Strobe.Color);
        UseProfileHardwareValue = Strobe.HardwareValue is null;
        HardwareValue = Strobe.HardwareValue ?? 0;
        OnPropertyChanged(nameof(PreviewColorHex));
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

    private void OnColorEdited()
    {
        OnPropertyChanged(nameof(PreviewColorHex));
        MarkDirty();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsStrobeAllowed));
        OnPropertyChanged(nameof(IsStrobeDisabled));
        OnPropertyChanged(nameof(MaxStrobeRateHz));
        OnPropertyChanged(nameof(MaxRateText));
        OnPropertyChanged(nameof(HardwareLimitText));
        OnRateOrSettingsChanged();
    }

    private void OnRateOrSettingsChanged()
    {
        OnPropertyChanged(nameof(IsRateCapped));
        OnPropertyChanged(nameof(ShowRateCap));
        OnPropertyChanged(nameof(RateCapText));
        OnPropertyChanged(nameof(EffectiveRateHz));
        OnPropertyChanged(nameof(SpeedText));
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        PreviewPoints = EffectPreviewMath.StrobeCurve(EffectiveRateHz, _dutyPercent / 100.0);
        OnPropertyChanged(nameof(PreviewPoints));
        OnPropertyChanged(nameof(PreviewCaption));
    }
}
