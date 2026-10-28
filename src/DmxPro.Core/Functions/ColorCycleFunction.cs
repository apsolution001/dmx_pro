using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// Fixtures cycle through a list of colours: each colour holds for <see cref="HoldSeconds"/>, then crossfades to the
/// next over <see cref="FadeSeconds"/>, looping until stopped. Only fixtures with colour channels take part (spec §4:
/// no colour-wheel/macro assumptions). With <see cref="FixtureOffsetSeconds"/> each fixture runs that much behind the
/// previous one, which turns the cycle into a colour chase across the fixtures.
/// <para>Legacy, load-only: converted on load by <see cref="Projects.LegacyFunctionMigration"/> (only Scene and Chaser
/// remain, owner decision 2026-10-28). Kept so old project files still deserialize; nothing creates it any more.</para>
/// </summary>
public sealed class ColorCycleFunction : FixtureEffectFunction
{
    public const double DefaultHoldSeconds = 1.0;
    public const double DefaultFadeSeconds = 1.0;

    [JsonIgnore]
    public override FunctionType Type => FunctionType.ColorCycle;

    /// <summary>Colours in play order. A new colour cycle starts with red, green, blue.</summary>
    public List<RgbColor> Colors { get; set; } = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255)];

    public double HoldSeconds { get; set; } = DefaultHoldSeconds;

    public double FadeSeconds { get; set; } = DefaultFadeSeconds;

    /// <summary>Delay of each fixture behind the previous one in <see cref="FixtureEffectFunction.FixtureIds"/>; 0 = all in sync.</summary>
    public double FixtureOffsetSeconds { get; set; }

    protected override LightFunction CloneCore() => CopyEffectTo(new ColorCycleFunction
    {
        Colors = [.. Colors],
        HoldSeconds = HoldSeconds,
        FadeSeconds = FadeSeconds,
        FixtureOffsetSeconds = FixtureOffsetSeconds,
    });
}
