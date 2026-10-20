using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// Strobe effect. Safety rule 8: the engine plays it only while "allow strobe" is on and never faster than the global
/// maximum strobe rate; otherwise the fixtures show steady light in <see cref="Color"/> and the engine reports why.
/// </summary>
public sealed class StrobeFunction : FixtureEffectFunction
{
    public const double DefaultRateHz = 5.0;
    public const double DefaultDutyCycle = 0.5;
    public const double MinDutyCycle = 0.05;
    public const double MaxDutyCycle = 0.95;

    [JsonIgnore]
    public override FunctionType Type => FunctionType.Strobe;

    public StrobeMode Mode { get; set; } = StrobeMode.Software;

    /// <summary>Flashes per second (software strobe). Capped by the engine's maximum strobe rate.</summary>
    public double RateHz { get; set; } = DefaultRateHz;

    /// <summary>Part of each flash period the light is on, <see cref="MinDutyCycle"/>..<see cref="MaxDutyCycle"/> (software strobe).</summary>
    public double DutyCycle { get; set; } = DefaultDutyCycle;

    public RgbColor Color { get; set; } = new(255, 255, 255);

    /// <summary>
    /// Raw value for the fixture's Strobe channel in <see cref="StrobeMode.Hardware"/> (read it from the fixture's DMX
    /// chart). Null = the start of the channel's first range that is not marked safe (usually the slowest strobe).
    /// </summary>
    public byte? HardwareValue { get; set; }

    protected override LightFunction CloneCore() => CopyEffectTo(new StrobeFunction
    {
        Mode = Mode,
        RateHz = RateHz,
        DutyCycle = DutyCycle,
        Color = Color,
        HardwareValue = HardwareValue,
    });
}
