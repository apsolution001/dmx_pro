namespace DmxPro.Engine.Runtime;

/// <summary>
/// Accumulates the contributions of all functions in one <see cref="EngineLayer"/> for one tick.
/// Intensity channels merge HTP (highest weighted value wins); all other channels merge LTP (latest start wins).
/// Also used as the scratch target of one chaser slot (<see cref="StepMix.Slot"/>); it then keeps the list of channels
/// written so far, so the chaser reads and clears only those.
/// </summary>
internal sealed class LayerBuffer
{
    private readonly bool[] _isIntensity;
    private readonly int[] _touched;
    private int _touchedCount;
    private StepMix? _mix;

    /// <param name="isIntensity">Per flat channel: merges HTP (true) or LTP.</param>
    /// <param name="mix">
    /// Chaser scratch for programs rendering into this buffer; the model's layers share one (rendering is sequential).
    /// Null = created on first use.
    /// </param>
    public LayerBuffer(bool[] isIntensity, StepMix? mix = null)
    {
        _isIntensity = isIntensity;
        _mix = mix;
        Htp = new float[isIntensity.Length];
        Weight = new float[isIntensity.Length];
        Ltp = new byte[isIntensity.Length];
        LtpStamp = new double[isIntensity.Length];
        LtpSeq = new long[isIntensity.Length];
        _touched = new int[isIntensity.Length];
    }

    /// <summary>HTP result for intensity channels: max of value × weight.</summary>
    public float[] Htp { get; }

    /// <summary>Highest weight (fade level × intensity) of any contributor; 0 = channel untouched by this layer.</summary>
    public float[] Weight { get; }

    /// <summary>LTP result for non-intensity channels.</summary>
    public byte[] Ltp { get; }

    /// <summary>Channels written since the last clear, in first-write order (each once).</summary>
    public ReadOnlySpan<int> Touched => _touched.AsSpan(0, _touchedCount);

    /// <summary>
    /// Scratch for a chaser rendering into this buffer (one nesting level deeper). Allocated on first use unless the
    /// model preallocated it (<see cref="StepMix.EnsureDepth"/>).
    /// </summary>
    public StepMix Mix => _mix ??= new StepMix(_isIntensity);

    private double[] LtpStamp { get; }

    private long[] LtpSeq { get; }

    public void Clear()
    {
        Array.Clear(Htp);
        Array.Clear(Weight);
        _touchedCount = 0;
    }

    /// <summary>Clears only the channels written since the last clear (scratch use: few channels, every slot).</summary>
    public void ClearTouched()
    {
        for (var i = 0; i < _touchedCount; i++)
        {
            var channel = _touched[i];
            Htp[channel] = 0f;
            Weight[channel] = 0f;
        }

        _touchedCount = 0;
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

        if (previousWeight <= 0f)
        {
            _touched[_touchedCount++] = channel;
        }

        if (weight > previousWeight)
        {
            Weight[channel] = weight;
        }
    }
}
