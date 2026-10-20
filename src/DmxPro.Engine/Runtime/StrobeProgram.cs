using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// A compiled <see cref="StrobeFunction"/> (safety rule 8). Software: the colour is on for <c>duty</c> of each period
/// and off (written as 0) for the rest, at <c>min(rate × speed, max strobe rate)</c> in real time. Hardware: the
/// fixture's Strobe channel gets the hardware value and the colour stays on; fixtures without a usable Strobe channel
/// fall back to software. While strobe is not allowed (or the maximum rate is 0) every fixture shows steady colour and
/// <see cref="StrobeLimit.Suppressed"/> is reported; a capped rate reports <see cref="StrobeLimit.RateLimited"/>.
/// </summary>
internal sealed class StrobeProgram : FunctionProgram
{
    private readonly EffectFixture[] _fixtures;
    private readonly RgbColor _color;
    private readonly double _rate;
    private readonly double _duty;
    private readonly bool _hardware;
    private readonly short _hardwareValue;
    private readonly bool _useWhite;

    /// <param name="id">Function id.</param>
    /// <param name="fixtures">Resolved fixtures.</param>
    /// <param name="mode">Software or hardware strobe.</param>
    /// <param name="rateHz">Requested software rate.</param>
    /// <param name="dutyCycle">On-part of each period.</param>
    /// <param name="color">Colour while on.</param>
    /// <param name="hardwareValue">User value for Strobe channels; null = documented strobing start per channel.</param>
    /// <param name="useWhite">Colour policy.</param>
    public StrobeProgram(Guid id, EffectFixture[] fixtures, StrobeMode mode, double rateHz, double dutyCycle, RgbColor color, byte? hardwareValue, bool useWhite)
        : base(id, 0, 0)
    {
        _fixtures = fixtures;
        _hardware = mode == StrobeMode.Hardware;
        _rate = double.IsFinite(rateHz) && rateHz > 0 ? rateHz : 0;
        _duty = Math.Clamp(double.IsFinite(dutyCycle) ? dutyCycle : StrobeFunction.DefaultDutyCycle, StrobeFunction.MinDutyCycle, StrobeFunction.MaxDutyCycle);
        _color = color;
        _hardwareValue = hardwareValue is { } value ? value : (short)-1;
        _useWhite = useWhite;
    }

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        var allowed = context.AllowStrobe && context.MaxStrobeRateHz > 0;
        var softwareOn = SoftwareLevel(elapsedSeconds, allowed, context, out var limit);
        var usedSoftware = false;

        for (var f = 0; f < _fixtures.Length; f++)
        {
            var fixture = _fixtures[f];
            var level = softwareOn;
            if (_hardware && WriteHardware(fixture, layer, weight, stamp, seq))
            {
                // Hardware strobe channels are forced to their safe value by the merge while strobe is not allowed.
                level = 1f;
            }
            else
            {
                usedSoftware = true;
            }

            fixture.WriteColor(layer, _color.R, _color.G, _color.B, level, _useWhite, weight, stamp, seq);
        }

        if (_fixtures.Length > 0)
        {
            context.StrobeLimit |= !allowed ? StrobeLimit.Suppressed
                : usedSoftware ? limit
                : StrobeLimit.None;
        }
    }

    /// <summary>Software on/off level (1 = on) and the rate limit applied.</summary>
    private float SoftwareLevel(double elapsedSeconds, bool allowed, RenderContext context, out StrobeLimit limit)
    {
        limit = StrobeLimit.None;
        if (!allowed || _rate <= 0 || !double.IsFinite(elapsedSeconds))
        {
            return 1f;
        }

        // Function time runs at `speed`; the cap applies to the real flash rate (rate × speed).
        var speed = context.Speed;
        var realRate = speed > 0 ? _rate * speed : _rate;
        var effective = realRate;
        if (realRate > context.MaxStrobeRateHz)
        {
            effective = context.MaxStrobeRateHz;
            limit = StrobeLimit.RateLimited;
        }

        var cycles = speed > 0 ? elapsedSeconds / speed * effective : elapsedSeconds * effective;
        var phase = cycles - Math.Floor(cycles);
        return phase < _duty ? 1f : 0f;
    }

    /// <summary>Writes the hardware strobe value on the fixture's Strobe channels; false when it has no usable one.</summary>
    private bool WriteHardware(EffectFixture fixture, LayerBuffer layer, float weight, double stamp, long seq)
    {
        var wrote = false;
        for (var i = 0; i < fixture.Strobe.Length; i++)
        {
            var value = _hardwareValue >= 0 ? _hardwareValue : fixture.StrobeActive[i];
            if (value >= 0)
            {
                layer.Write(fixture.Strobe[i], (float)value, weight, stamp, seq);
                wrote = true;
            }
        }

        return wrote;
    }
}
