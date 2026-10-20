namespace DmxPro.Engine.Runtime;

/// <summary>
/// One fixture's colour/intensity channels for effects, resolved from its profile mode by channel type (never by offset)
/// to flat channel indexes. Immutable; implements the colour policy of <see cref="Core.Functions.FixtureEffectFunction"/>.
/// </summary>
internal sealed class EffectFixture
{
    public EffectFixture(int[] dimmer, int[] red, int[] green, int[] blue, int[] white, int[] amber, int[] uv, int[] strobe, short[] strobeActive)
    {
        Dimmer = dimmer;
        Red = red;
        Green = green;
        Blue = blue;
        White = white;
        Amber = amber;
        Uv = uv;
        Strobe = strobe;
        StrobeActive = strobeActive;
    }

    public int[] Dimmer { get; }

    public int[] Red { get; }

    public int[] Green { get; }

    public int[] Blue { get; }

    public int[] White { get; }

    public int[] Amber { get; }

    public int[] Uv { get; }

    /// <summary>Hardware strobe channels of the mode.</summary>
    public int[] Strobe { get; }

    /// <summary>
    /// Per <see cref="Strobe"/> channel: start of its first capability not marked safe (a documented strobing value), or
    /// -1 when the profile documents none (then the meaning is unknown and only a user-given value is used).
    /// </summary>
    public short[] StrobeActive { get; }

    public bool HasRgb => Red.Length > 0 || Green.Length > 0 || Blue.Length > 0;

    /// <summary>True when the fixture has any colour channel (R, G, B or W) an effect colour can be shown on.</summary>
    public bool HasColor => HasRgb || White.Length > 0;

    public bool HasDimmer => Dimmer.Length > 0;

    /// <summary>True when there is anything to light (colour or dimmer).</summary>
    public bool IsEmpty => !HasColor && !HasDimmer;

    /// <summary>
    /// Writes a colour (components 0..255) at <paramref name="level"/> 0..1: Dimmer carries the level when present
    /// (colour at full), else the colour channels are scaled. Amber/UV are written 0; White per
    /// <paramref name="useWhite"/>. Dimmer-only fixtures get the brightest component × level on the Dimmer.
    /// </summary>
    public void WriteColor(LayerBuffer layer, float r, float g, float b, float level, bool useWhite, float weight, double stamp, long seq)
    {
        var brightest = MathF.Max(r, MathF.Max(g, b));
        var colorLevel = level;
        if (HasDimmer)
        {
            Write(layer, Dimmer, HasColor ? 255f * level : brightest * level, weight, stamp, seq);
            colorLevel = 1f;
        }

        if (!HasRgb)
        {
            Write(layer, White, brightest * colorLevel, weight, stamp, seq);
            return;
        }

        var w = 0f;
        if (useWhite && White.Length > 0)
        {
            w = MathF.Min(r, MathF.Min(g, b));
            r -= w;
            g -= w;
            b -= w;
        }

        Write(layer, Red, r * colorLevel, weight, stamp, seq);
        Write(layer, Green, g * colorLevel, weight, stamp, seq);
        Write(layer, Blue, b * colorLevel, weight, stamp, seq);
        Write(layer, White, w * colorLevel, weight, stamp, seq);
        Write(layer, Amber, 0f, weight, stamp, seq);
        Write(layer, Uv, 0f, weight, stamp, seq);
    }

    /// <summary>Writes only the Dimmer channels at <paramref name="level"/> 0..1 (nothing when the mode has none).</summary>
    public void WriteDimmer(LayerBuffer layer, float level, float weight, double stamp, long seq) =>
        Write(layer, Dimmer, 255f * level, weight, stamp, seq);

    private static void Write(LayerBuffer layer, int[] channels, float value, float weight, double stamp, long seq)
    {
        for (var i = 0; i < channels.Length; i++)
        {
            layer.Write(channels[i], value, weight, stamp, seq);
        }
    }
}
