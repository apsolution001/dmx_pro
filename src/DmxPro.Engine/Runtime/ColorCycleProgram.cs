using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// A compiled <see cref="ColorCycleFunction"/>: per colour slot (hold + fade) the colour holds, then crossfades linearly
/// to the next; the list loops. Fixture i runs <c>i × offset</c> behind the first.
/// </summary>
internal sealed class ColorCycleProgram : FunctionProgram
{
    private readonly EffectFixture[] _fixtures;
    private readonly float[] _red;
    private readonly float[] _green;
    private readonly float[] _blue;
    private readonly double _hold;
    private readonly double _fade;
    private readonly double _slot;
    private readonly double _offset;
    private readonly bool _useWhite;

    public ColorCycleProgram(Guid id, EffectFixture[] fixtures, IReadOnlyList<RgbColor> colors, double holdSeconds, double fadeSeconds, double offsetSeconds, bool useWhite)
        : base(id, 0, 0)
    {
        ArgumentNullException.ThrowIfNull(colors);
        _fixtures = fixtures;
        _red = new float[colors.Count];
        _green = new float[colors.Count];
        _blue = new float[colors.Count];
        for (var i = 0; i < colors.Count; i++)
        {
            _red[i] = colors[i].R;
            _green[i] = colors[i].G;
            _blue[i] = colors[i].B;
        }

        _hold = SanitizeSeconds(holdSeconds);
        _fade = SanitizeSeconds(fadeSeconds);
        _slot = Math.Max(_hold + _fade, StepProgram.MinStepSeconds);
        _hold = _slot - _fade;
        _offset = double.IsFinite(offsetSeconds) ? offsetSeconds : 0;
        _useWhite = useWhite;
    }

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        var count = _red.Length;
        if (count == 0 || !double.IsFinite(elapsedSeconds))
        {
            return;
        }

        var cycle = count * _slot;
        for (var f = 0; f < _fixtures.Length; f++)
        {
            var position = Wrap(elapsedSeconds - (f * _offset), cycle);
            var index = Math.Min((int)(position / _slot), count - 1);
            var within = position - (index * _slot);
            float r = _red[index], g = _green[index], b = _blue[index];
            if (_fade > 0 && within > _hold && count > 1)
            {
                var next = (index + 1) % count;
                var p = (float)Math.Min(1, (within - _hold) / _fade);
                r += (_red[next] - r) * p;
                g += (_green[next] - g) * p;
                b += (_blue[next] - b) * p;
            }

            _fixtures[f].WriteColor(layer, r, g, b, 1f, _useWhite, weight, stamp, seq);
        }
    }
}
