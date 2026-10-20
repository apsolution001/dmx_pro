using DmxPro.Core.Profiles;

namespace DmxPro.Core.Stage;

/// <summary>
/// How the virtual stage reads a strobe channel. Each value 0–255 is classified once (at build time) into a flash rate:
/// 0 = no strobe (light steady), &gt; 0 = strobing at that many Hz, &lt; 0 = shutter closed (dark).
/// </summary>
/// <remarks>
/// With capabilities, the first range containing the value decides, classified by the shared
/// <see cref="StrobeCapabilityClassifier"/> the engine's strobe safety uses too: Strobing ranges flash slow → fast from
/// their Min to their Max, Closed ranges are dark, Steady and Unknown ranges shine steadily (an unrecognised range is
/// never presented as a confirmed strobe, safety rule 7). Values no range covers (or channels without capabilities) fall
/// back to the profile default, which is the engine's resting value too: at or below the default = steady, above =
/// strobing over the rest of the 0–255 range.
/// </remarks>
internal static class StageColorStrobe
{
    /// <summary>Slowest flash rate (start of a strobing range).</summary>
    public const float MinRateHz = 1f;

    /// <summary>Fastest flash rate (end of a strobing range).</summary>
    public const float MaxRateHz = 20f;

    /// <summary>The view never flashes faster than this.</summary>
    public const float MaxVisualRateHz = 25f;

    /// <summary>Share of each period the light is on.</summary>
    public const double OnFraction = 1.0 / 3.0;

    /// <summary>Rate marker for a closed shutter.</summary>
    public const float Closed = -1f;

    /// <summary>Flash rate per strobe value (256 entries).</summary>
    public static float[] BuildRateTable(ChannelDefinition channel)
    {
        var rates = new float[256];
        var kinds = new StrobeRangeKind[channel.Capabilities.Count];
        for (var i = 0; i < kinds.Length; i++)
        {
            kinds[i] = StrobeCapabilityClassifier.Classify(channel.Capabilities[i]);
        }

        for (var value = 0; value < rates.Length; value++)
        {
            rates[value] = RateFor((byte)value, channel, kinds);
        }

        return rates;
    }

    /// <summary>1 when the light is in the on part of the flash at <paramref name="timeSeconds"/>, otherwise 0.</summary>
    public static float Phase(float rateHz, double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds))
        {
            timeSeconds = 0;
        }

        var cycles = timeSeconds * Math.Min(rateHz, MaxVisualRateHz);
        var fraction = cycles - Math.Floor(cycles);
        return fraction < OnFraction ? 1f : 0f;
    }

    private static float RateFor(byte value, ChannelDefinition channel, StrobeRangeKind[] kinds)
    {
        for (var i = 0; i < kinds.Length; i++)
        {
            var capability = channel.Capabilities[i];
            if (!capability.Contains(value))
            {
                continue;
            }

            return kinds[i] switch
            {
                StrobeRangeKind.Strobing => Linear(value, capability.Min, capability.Max),
                StrobeRangeKind.Closed => Closed,
                _ => 0f, // Steady, or Unknown: never shown as a confirmed strobe (safety rule 7).
            };
        }

        return value <= channel.Default ? 0f : Linear(value, channel.Default + 1, 255);
    }

    private static float Linear(int value, int min, int max)
    {
        min = Math.Clamp(min, 0, 255);
        max = Math.Clamp(max, min, 255);
        var t = max == min ? 1f : (value - min) / (float)(max - min);
        return MinRateHz + ((MaxRateHz - MinRateHz) * Math.Clamp(t, 0f, 1f));
    }
}
