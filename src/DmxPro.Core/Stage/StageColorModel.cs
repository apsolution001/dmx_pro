using DmxPro.Core.Profiles;

namespace DmxPro.Core.Stage;

/// <summary>
/// Turns one fixture's DMX values into what the light looks like (<see cref="StageLightState"/>), profile-driven:
/// channel offsets are resolved once from the profile mode by channel type (never fixed positions). Built once per
/// fixture when the patch changes; <see cref="Evaluate"/> runs per frame and allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// Colour: additive mix (components 0..1) of Red/Green/Blue, White (adds to all three), Amber (1, 0.75, 0) and UV
/// (0.4, 0, 0.8) — the same weights as <see cref="Functions.ScenePreviewColor"/>, so swatches and the stage agree. The
/// hue is the mix normalised so its brightest component is 255; the intensity is that brightest component (clamped to
/// 1), times Dimmer/255 when the mode has a dimmer. A dimmer-only fixture shows warm white.
/// </para>
/// <para>
/// Strobe: the strobe value is classified once per value (0–255 lookup table) at <see cref="Build"/>, see
/// <see cref="StageColorStrobe"/>. While strobing the intensity follows a square wave (on for the first third of each
/// period). The input is the engine's output snapshot, so Grand Master, Blackout and strobe safety are already applied
/// and are not re-applied here.
/// </para>
/// </remarks>
public sealed class StageColorModel
{
    /// <summary>Amber's green share (≈ 255,191,0).</summary>
    internal const float AmberGreen = 0.75f;

    /// <summary>UV's red share (deep violet, weaker than a full colour).</summary>
    internal const float UvRed = 0.4f;

    /// <summary>UV's blue share.</summary>
    internal const float UvBlue = 0.8f;

    /// <summary>Warm white of a fixture that has a dimmer but no colour channels.</summary>
    internal const byte WarmWhiteR = 255;

    /// <summary>Warm white green component.</summary>
    internal const byte WarmWhiteG = 240;

    /// <summary>Warm white blue component.</summary>
    internal const byte WarmWhiteB = 210;

    private const int UniverseSize = 512;

    private readonly int _dimmer = -1;
    private readonly int _red = -1;
    private readonly int _green = -1;
    private readonly int _blue = -1;
    private readonly int _white = -1;
    private readonly int _amber = -1;
    private readonly int _uv = -1;
    private readonly int _strobe = -1;

    /// <summary>Flash rate in Hz per strobe value (see <see cref="StageColorStrobe"/>); null when the mode has no strobe channel.</summary>
    private readonly float[]? _strobeRates;

    private readonly bool _hasColor;

    private StageColorModel(FixtureProfile profile, FixtureMode mode)
    {
        ChannelCount = mode.ChannelCount;
        ChannelDefinition? strobeChannel = null;
        for (var offset = 0; offset < mode.Channels.Count; offset++)
        {
            if (profile.FindChannel(mode.Channels[offset]) is not { } channel)
            {
                continue;
            }

            switch (channel.Type)
            {
                case ChannelType.Dimmer:
                    SetFirst(ref _dimmer, offset);
                    break;
                case ChannelType.Red:
                    SetFirst(ref _red, offset);
                    break;
                case ChannelType.Green:
                    SetFirst(ref _green, offset);
                    break;
                case ChannelType.Blue:
                    SetFirst(ref _blue, offset);
                    break;
                case ChannelType.White:
                    SetFirst(ref _white, offset);
                    break;
                case ChannelType.Amber:
                    SetFirst(ref _amber, offset);
                    break;
                case ChannelType.UV:
                    SetFirst(ref _uv, offset);
                    break;
                case ChannelType.Strobe when _strobe < 0:
                    _strobe = offset;
                    strobeChannel = channel;
                    break;
            }
        }

        _hasColor = _red >= 0 || _green >= 0 || _blue >= 0 ||
                    _white >= 0 || _amber >= 0 || _uv >= 0;
        HasLightOutput = _hasColor || _dimmer >= 0;
        _strobeRates = strobeChannel is null ? null : StageColorStrobe.BuildRateTable(strobeChannel);
    }

    /// <summary>Number of DMX channels of the mode (footprint).</summary>
    public int ChannelCount { get; private init; }

    /// <summary>True when the fixture has any colour or dimmer channel (otherwise it is drawn as an unknown light).</summary>
    public bool HasLightOutput { get; private init; }

    /// <summary>Resolves the channel layout of a profile mode.</summary>
    public static StageColorModel Build(FixtureProfile profile, FixtureMode mode)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(mode);

        return new StageColorModel(profile, mode);
    }

    /// <summary>
    /// The look of the fixture. <paramref name="universe"/> is the 512 output values of its universe (index 0 = DMX
    /// address 1; may be empty = all 0); <paramref name="startAddress"/> is 1-based; <paramref name="timeSeconds"/> is a
    /// monotonic clock used for the strobe phase.
    /// </summary>
    public StageLightState Evaluate(ReadOnlySpan<byte> universe, int startAddress, double timeSeconds)
    {
        if (!HasLightOutput || startAddress < 1 || startAddress > UniverseSize)
        {
            return StageLightState.Off;
        }

        var start = startAddress - 1;
        byte r, g, b;
        float intensity;
        if (_hasColor)
        {
            var white = Level(universe, start, _white);
            var amber = Level(universe, start, _amber);
            var uv = Level(universe, start, _uv);
            var red = Level(universe, start, _red) + white + amber + (uv * UvRed);
            var green = Level(universe, start, _green) + white + (amber * AmberGreen);
            var blue = Level(universe, start, _blue) + white + (uv * UvBlue);
            var max = MathF.Max(red, MathF.Max(green, blue));
            if (!(max > 0f))
            {
                return StageLightState.Off;
            }

            r = ToByte(red / max);
            g = ToByte(green / max);
            b = ToByte(blue / max);
            intensity = MathF.Min(max, 1f);
        }
        else
        {
            r = WarmWhiteR;
            g = WarmWhiteG;
            b = WarmWhiteB;
            intensity = 1f;
        }

        if (_dimmer >= 0)
        {
            intensity *= Level(universe, start, _dimmer);
        }

        var strobing = false;
        if (_strobeRates is { } rates)
        {
            var rate = rates[Read(universe, start, _strobe)];
            if (rate < 0f)
            {
                intensity = 0f;
            }
            else if (rate > 0f)
            {
                strobing = true;
                intensity *= StageColorStrobe.Phase(rate, timeSeconds);
            }
        }

        if (!(intensity > 0f))
        {
            intensity = 0f;
        }

        return new StageLightState(r, g, b, MathF.Min(intensity, 1f), strobing);
    }

    private static void SetFirst(ref int field, int offset)
    {
        if (field < 0)
        {
            field = offset;
        }
    }

    /// <summary>Value of the channel at <paramref name="offset"/>; 0 when the channel is absent or outside the universe.</summary>
    private static byte Read(ReadOnlySpan<byte> universe, int start, int offset)
    {
        if (offset < 0)
        {
            return 0;
        }

        var index = start + offset;
        return (uint)index < (uint)universe.Length && index < UniverseSize ? universe[index] : (byte)0;
    }

    private static float Level(ReadOnlySpan<byte> universe, int start, int offset) => Read(universe, start, offset) / 255f;

    private static byte ToByte(float level) => level > 0f ? (byte)MathF.Round(MathF.Min(level, 1f) * 255f) : (byte)0;
}
