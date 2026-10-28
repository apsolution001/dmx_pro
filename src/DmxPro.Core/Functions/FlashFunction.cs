using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// A temporary look: the fixtures go to <see cref="Color"/> at full intensity, instantly, and release instantly when the
/// function stops (no fades, so button-up is exact; safety rule 5). With <see cref="DurationSeconds"/> &gt; 0 a trigger
/// also ends the flash by itself after that time; 0 means "while held / until stopped".
/// <para>Legacy, load-only: converted on load by <see cref="Projects.LegacyFunctionMigration"/> (only Scene and Chaser
/// remain, owner decision 2026-10-28). Kept so old project files still deserialize; nothing creates it any more.</para>
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
