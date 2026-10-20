using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>A compiled <see cref="PulseFunction"/>: level = min + (max − min) × shape(phase), starting at the minimum.</summary>
internal sealed class PulseProgram : FunctionProgram
{
    private readonly EffectFixture[] _fixtures;
    private readonly RgbColor? _color;
    private readonly PulseShape _shape;
    private readonly double _period;
    private readonly float _min;
    private readonly float _max;
    private readonly bool _useWhite;

    public PulseProgram(Guid id, EffectFixture[] fixtures, RgbColor? color, PulseShape shape, double periodSeconds, double minLevel, double maxLevel, bool useWhite)
        : base(id, 0, 0)
    {
        _fixtures = fixtures;
        _color = color;
        _shape = shape;
        _period = double.IsFinite(periodSeconds)
            ? Math.Max(periodSeconds, PulseFunction.MinPeriodSeconds)
            : PulseFunction.DefaultPeriodSeconds;
        _min = (float)SanitizeLevel(minLevel, 0);
        _max = (float)SanitizeLevel(maxLevel, 1);
        _useWhite = useWhite;
    }

    /// <summary>Pulse level 0..1 at function time <paramref name="elapsedSeconds"/>.</summary>
    public float LevelAt(double elapsedSeconds)
    {
        var phase = double.IsFinite(elapsedSeconds) ? Wrap(elapsedSeconds, _period) / _period : 0;
        var shape = _shape == PulseShape.Triangle
            ? (phase < 0.5 ? 2 * phase : 2 - (2 * phase))
            : 0.5 - (0.5 * Math.Cos(2 * Math.PI * phase));
        return _min + ((_max - _min) * (float)shape);
    }

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        var level = LevelAt(elapsedSeconds);
        for (var f = 0; f < _fixtures.Length; f++)
        {
            var fixture = _fixtures[f];
            if (_color is { } color)
            {
                fixture.WriteColor(layer, color.R, color.G, color.B, level, _useWhite, weight, stamp, seq);
            }
            else
            {
                fixture.WriteDimmer(layer, level, weight, stamp, seq);
            }
        }
    }
}
