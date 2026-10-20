namespace DmxPro.Engine.Runtime;

/// <summary>
/// Accumulates the contributions of all functions in one <see cref="EngineLayer"/> for one tick.
/// Intensity channels merge HTP (highest weighted value wins); all other channels merge LTP (latest start wins).
/// </summary>
internal sealed class LayerBuffer
{
    private readonly bool[] _isIntensity;

    public LayerBuffer(bool[] isIntensity)
    {
        _isIntensity = isIntensity;
        Htp = new float[isIntensity.Length];
        Weight = new float[isIntensity.Length];
        Ltp = new byte[isIntensity.Length];
        LtpStamp = new double[isIntensity.Length];
        LtpSeq = new long[isIntensity.Length];
    }

    /// <summary>HTP result for intensity channels: max of value × weight.</summary>
    public float[] Htp { get; }

    /// <summary>Highest weight (fade level × intensity) of any contributor; 0 = channel untouched by this layer.</summary>
    public float[] Weight { get; }

    /// <summary>LTP result for non-intensity channels.</summary>
    public byte[] Ltp { get; }

    private double[] LtpStamp { get; }

    private long[] LtpSeq { get; }

    public void Clear()
    {
        Array.Clear(Htp);
        Array.Clear(Weight);
    }

    /// <summary>Adds one contribution. <paramref name="stamp"/>/<paramref name="seq"/> order LTP (later wins).</summary>
    public void Write(int channel, byte value, float weight, double stamp, long seq) =>
        Write(channel, (float)value, weight, stamp, seq);

    /// <summary>
    /// Adds one contribution with a fractional value (effects, interpolation): intensity channels keep the precision
    /// until the final rounding; LTP channels are rounded and clamped to 0–255 here.
    /// </summary>
    public void Write(int channel, float value, float weight, double stamp, long seq)
    {
        if (weight <= 0f)
        {
            return;
        }

        var previousWeight = Weight[channel];
        if (_isIntensity[channel])
        {
            var weighted = Math.Clamp(value, 0f, 255f) * weight;
            if (weighted > Htp[channel])
            {
                Htp[channel] = weighted;
            }
        }
        else if (previousWeight <= 0f
            || stamp > LtpStamp[channel]
            || (stamp == LtpStamp[channel] && seq >= LtpSeq[channel]))
        {
            Ltp[channel] = DmxEngine.ToByte(value);
            LtpStamp[channel] = stamp;
            LtpSeq[channel] = seq;
        }

        if (weight > previousWeight)
        {
            Weight[channel] = weight;
        }
    }
}
