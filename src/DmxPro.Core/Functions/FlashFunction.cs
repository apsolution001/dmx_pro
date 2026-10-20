using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// A temporary look: the fixtures go to <see cref="Color"/> at full intensity, instantly, and release instantly when the
/// function stops (no fades, so button-up is exact; safety rule 5). With <see cref="DurationSeconds"/> &gt; 0 a trigger
/// also ends the flash by itself after that time; 0 means "while held / until stopped".
/// </summary>
public sealed class FlashFunction : FixtureEffectFunction
{
    [JsonIgnore]
    public override FunctionType Type => FunctionType.Flash;

    public RgbColor Color { get; set; } = new(255, 255, 255);

    /// <summary>Auto-release time in seconds; 0 = until stopped.</summary>
    public double DurationSeconds { get; set; }

    protected override LightFunction CloneCore() => CopyEffectTo(new FlashFunction
    {
        Color = Color,
        DurationSeconds = DurationSeconds,
    });
}
