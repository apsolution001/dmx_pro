namespace DmxPro.Audio.Analysis;

/// <summary>Waveform envelope for drawing (pure maths, no I/O).</summary>
public static class WaveformPeaks
{
    /// <summary>
    /// Peak absolute amplitude per bucket of <c>sampleRate / peaksPerSecond</c> samples (the last bucket may be partial),
    /// clamped 0..1. Sample <c>n</c> falls in bucket <c>floor(n * peaksPerSecond / sampleRate)</c>, so fractional
    /// bucket sizes are spread evenly. Empty input gives an empty array.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A rate is not positive, or <paramref name="peaksPerSecond"/> exceeds <paramref name="sampleRate"/>.
    /// </exception>
    public static float[] Compute(ReadOnlySpan<float> mono, int sampleRate, int peaksPerSecond)
    {
        var expected = sampleRate > 0 ? (int)Math.Min(int.MaxValue / 2, ((long)mono.Length * Math.Max(peaksPerSecond, 0) / sampleRate) + 1) : 0;
        var accumulator = new WaveformPeakAccumulator(sampleRate, peaksPerSecond, expected);
        accumulator.Add(mono);
        return accumulator.ToArray();
    }
}
