using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// Repeating brightness rise and fall ("breathing") between <see cref="MinLevel"/> and <see cref="MaxLevel"/>, one full
/// cycle every <see cref="PeriodSeconds"/>. With a <see cref="Color"/> the fixtures show that colour (on the Dimmer when
/// the mode has one, else by scaling the colour channels). Without a colour only the Dimmer pulses and the colour comes
/// from what plays underneath; fixtures without a Dimmer are then left alone (no safe way to scale an unknown colour).
/// <para>Legacy, load-only: converted on load by <see cref="Projects.LegacyFunctionMigration"/> (only Scene and Chaser
/// remain, owner decision 2026-10-28). Kept so old project files still deserialize; nothing creates it any more.</para>
/// </summary>
public sealed class PulseFunction : FixtureEffectFunction
{
    public const double DefaultPeriodSeconds = 2.0;

    /// <summary>Shortest cycle the engine plays (faster settings are slowed down to this): keeps pulses from flickering.</summary>
    public const double MinPeriodSeconds = 0.1;

    [JsonIgnore]
    public override FunctionType Type => FunctionType.Pulse;

    /// <summary>
    /// Colour of the pulse; null = dimmer-only pulse. Defaults to white. Always written (also as <c>null</c>): the project
    /// JSON skips nulls by default, which would reload a dimmer-only pulse as white.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public RgbColor? Color { get; set; } = new RgbColor(255, 255, 255);

    public PulseShape Shape { get; set; } = PulseShape.Sine;

    /// <summary>Time of one full low → high → low cycle.</summary>
    public double PeriodSeconds { get; set; } = DefaultPeriodSeconds;

    /// <summary>Lowest level, 0..1.</summary>
    public double MinLevel { get; set; }

    /// <summary>Highest level, 0..1.</summary>
    public double MaxLevel { get; set; } = 1.0;

    protected override LightFunction CloneCore() => CopyEffectTo(new PulseFunction
    {
        Color = Color,
        Shape = Shape,
        PeriodSeconds = PeriodSeconds,
        MinLevel = MinLevel,
        MaxLevel = MaxLevel,
    });
}
