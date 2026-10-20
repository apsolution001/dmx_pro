using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// A compiled <see cref="FlashFunction"/>: the colour at full level, no fades in or out (instant on, instant release),
/// optionally only for a fixed duration.
/// </summary>
internal sealed class FlashProgram : FunctionProgram
{
    private readonly EffectFixture[] _fixtures;
    private readonly RgbColor _color;
    private readonly double _duration;
    private readonly bool _useWhite;

    public FlashProgram(Guid id, EffectFixture[] fixtures, RgbColor color, double durationSeconds, bool useWhite)
        : base(id, 0, 0)
    {
        _fixtures = fixtures;
        _color = color;
        _duration = SanitizeSeconds(durationSeconds);
        _useWhite = useWhite;
    }

    public override double DurationSeconds => _duration > 0 ? _duration : double.PositiveInfinity;

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        if (_duration > 0 && !(elapsedSeconds < _duration))
        {
            return;
        }

        for (var f = 0; f < _fixtures.Length; f++)
        {
            _fixtures[f].WriteColor(layer, _color.R, _color.G, _color.B, 1f, _useWhite, weight, stamp, seq);
        }
    }
}
