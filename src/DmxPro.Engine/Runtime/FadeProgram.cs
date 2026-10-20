using DmxPro.Core.Functions;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// A compiled <see cref="FadeFunction"/>. Per channel: in both scenes → linear interpolation at full weight; only in the
/// target → target value with weight × progress (cross-fades in from underneath); only in the source → source value with
/// weight × (1 − progress). Holds the target once the duration is over.
/// </summary>
internal sealed class FadeProgram : FunctionProgram
{
    /// <summary>Marker in the from/to tables for "this scene does not set the channel".</summary>
    public const short Absent = -1;

    private readonly int[] _channels;
    private readonly short[] _from;
    private readonly short[] _to;
    private readonly double _duration;

    public FadeProgram(Guid id, int[] channels, short[] from, short[] to, double durationSeconds)
        : base(id, 0, 0)
    {
        _channels = channels;
        _from = from;
        _to = to;
        _duration = SanitizeSeconds(durationSeconds);
    }

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        var progress = _duration <= 0 || !(elapsedSeconds < _duration)
            ? 1f
            : (float)Math.Max(0, elapsedSeconds / _duration);

        for (var i = 0; i < _channels.Length; i++)
        {
            var from = _from[i];
            var to = _to[i];
            if (from != Absent && to != Absent)
            {
                layer.Write(_channels[i], from + ((to - from) * progress), weight, stamp, seq);
            }
            else if (to != Absent)
            {
                layer.Write(_channels[i], (float)to, weight * progress, stamp, seq);
            }
            else if (from != Absent)
            {
                layer.Write(_channels[i], (float)from, weight * (1f - progress), stamp, seq);
            }
        }
    }
}
