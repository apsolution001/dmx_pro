namespace DmxPro.Engine.Runtime;

/// <summary>A compiled <see cref="Core.Functions.SceneFunction"/>: fixed values at resolved channels.</summary>
internal sealed class SceneProgram : FunctionProgram
{
    private readonly int[] _channels;
    private readonly byte[] _values;

    public SceneProgram(Guid id, double fadeInSeconds, double fadeOutSeconds, int[] channels, byte[] values)
        : base(id, fadeInSeconds, fadeOutSeconds)
    {
        _channels = channels;
        _values = values;
    }

    public override void Render(double elapsedSeconds, float weight, LayerBuffer layer, double stamp, long seq, RenderContext context)
    {
        for (var i = 0; i < _channels.Length; i++)
        {
            layer.Write(_channels[i], _values[i], weight, stamp, seq);
        }
    }
}
