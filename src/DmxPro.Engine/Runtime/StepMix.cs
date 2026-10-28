namespace DmxPro.Engine.Runtime;

/// <summary>
/// Scratch for one nesting level of chaser rendering: each playing slot is rendered at full level into
/// <see cref="Slot"/> and folded, oldest first, into a per-channel mix, which is then written into the real target once.
/// </summary>
/// <remarks>
/// Blending rule (owner request 2026-10-28; replaces the plain HTP max of both steps, which dipped to about half in the
/// middle of a crossfade):
/// <list type="bullet">
/// <item>A channel the incoming (current) step shares with an older step moves linearly from the value it had when the
/// incoming step started to the incoming value over the incoming step's fade-in: out + (in − out) × t. Equal values stay
/// steady. The older step's fade-out does not apply to shared channels (it is often 0 while the fade-in is set).</item>
/// <item>A channel only in the incoming step fades in from whatever is underneath over the fade-in.</item>
/// <item>A channel only in an older step fades out to whatever is underneath over that step's fade-out.</item>
/// <item>Non-intensity (LTP) channels never fade: the incoming value wins as soon as its fade-in has begun.</item>
/// </list>
/// Each channel is held as an affine map of what lies underneath (under × (1 − weight) + HTP), so nested chasers and
/// partly covering programs compose exactly. Engine thread only; sparse (touched channels only), allocation-free.
/// </remarks>
internal sealed class StepMix
{
    private readonly bool[] _isIntensity;
    private readonly float[] _htp;
    private readonly float[] _weight;
    private readonly float[] _level;
    private readonly float[] _fromLevel;
    private readonly byte[] _ltp;
    private readonly int[] _touched;
    private int _count;

    public StepMix(bool[] isIntensity)
    {
        _isIntensity = isIntensity;
        var n = isIntensity.Length;
        _htp = new float[n];
        _weight = new float[n];
        _level = new float[n];
        _fromLevel = new float[n];
        _ltp = new byte[n];
        _touched = new int[n];
        Slot = new LayerBuffer(isIntensity);
    }

    /// <summary>Render target of one slot; its own <see cref="LayerBuffer.Mix"/> is the next nesting level.</summary>
    public LayerBuffer Slot { get; }

    /// <summary>Preallocates <paramref name="depth"/> nesting levels including this one (model build, off the tick).</summary>
    public void EnsureDepth(int depth)
    {
        var mix = this;
        for (var level = 1; level < depth; level++)
        {
            mix = mix.Slot.Mix;
        }
    }

    /// <summary>
    /// Folds <see cref="Slot"/> as an older slot (fully faded in): it takes over the channels it writes.
    /// </summary>
    /// <param name="level">Its fade-out level now (applies to channels no newer step takes over).</param>
    /// <param name="fromLevel">Its fade-out level when the current slot started (start point of shared channels).</param>
    public void FoldTail(float level, float fromLevel)
    {
        var slot = Slot;
        var touched = slot.Touched;
        for (var i = 0; i < touched.Length; i++)
        {
            var channel = touched[i];
            if (_weight[channel] <= 0f)
            {
                _touched[_count++] = channel;
            }

            _htp[channel] = slot.Htp[channel];
            _weight[channel] = slot.Weight[channel];
            _ltp[channel] = slot.Ltp[channel];
            _level[channel] = level;
            _fromLevel[channel] = fromLevel;
        }

        slot.ClearTouched();
    }

    /// <summary>
    /// Folds <see cref="Slot"/> as the current slot at fade-in level <paramref name="fadeIn"/>: shared channels blend
    /// linearly from the older value, its own channels fade in from underneath.
    /// </summary>
    public void FoldCurrent(float fadeIn)
    {
        var slot = Slot;
        var touched = slot.Touched;
        for (var i = 0; i < touched.Length; i++)
        {
            var channel = touched[i];
            if (_weight[channel] > 0f)
            {
                var from = (1f - fadeIn) * _fromLevel[channel];
                _htp[channel] = (from * _htp[channel]) + (fadeIn * slot.Htp[channel]);
                _weight[channel] = (from * _weight[channel]) + (fadeIn * slot.Weight[channel]);
                if (fadeIn > 0f)
                {
                    _ltp[channel] = slot.Ltp[channel];
                }

                _level[channel] = 1f;
            }
            else
            {
                _touched[_count++] = channel;
                _htp[channel] = slot.Htp[channel];
                _weight[channel] = slot.Weight[channel];
                _ltp[channel] = slot.Ltp[channel];
                _level[channel] = fadeIn;
            }
        }

        slot.ClearTouched();
    }

    /// <summary>Writes the mix into <paramref name="target"/> scaled by <paramref name="weight"/>, then clears it.</summary>
    public void Emit(LayerBuffer target, float weight, double stamp, long seq)
    {
        for (var i = 0; i < _count; i++)
        {
            var channel = _touched[i];
            var full = _weight[channel];
            var effective = full * _level[channel];
            if (effective > 0f)
            {
                if (_isIntensity[channel])
                {
                    target.Write(channel, _htp[channel] / full, effective * weight, stamp, seq);
                }
                else
                {
                    target.Write(channel, _ltp[channel], effective * weight, stamp, seq);
                }
            }

            _htp[channel] = 0f;
            _weight[channel] = 0f;
        }

        _count = 0;
    }
}
