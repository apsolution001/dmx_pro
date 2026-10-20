using DmxPro.Audio.Analysis;

namespace DmxPro.Audio.Tests.Analysis;

public class WaveformPeaksTests
{
    [Fact]
    public void Compute_Empty_ReturnsEmptyArray()
    {
        Assert.Empty(WaveformPeaks.Compute(ReadOnlySpan<float>.Empty, 44100, 100));
    }

    [Fact]
    public void Compute_ExactBuckets_PeakAbsolutePerBucket()
    {
        float[] samples = [0.1f, -0.5f, 0.2f, 0.3f, -0.1f, 0.05f];

        var peaks = WaveformPeaks.Compute(samples, 3, 1);

        Assert.Equal([0.5f, 0.3f], peaks);
    }

    [Fact]
    public void Compute_PartialLastBucket_IsIncluded()
    {
        float[] samples = [0.1f, 0.2f, 0.3f, 0.4f, -0.9f];

        var peaks = WaveformPeaks.Compute(samples, 2, 1);

        Assert.Equal([0.2f, 0.4f, 0.9f], peaks);
    }

    [Fact]
    public void Compute_ValuesAboveOne_AreClamped()
    {
        float[] samples = [1.5f, -2f, 0.5f, 0.25f];

        var peaks = WaveformPeaks.Compute(samples, 2, 1);

        Assert.Equal([1f, 0.5f], peaks);
    }

    [Fact]
    public void Compute_NaNSamples_AreIgnored()
    {
        float[] samples = [float.NaN, 0.25f, float.NaN, float.NaN];

        var peaks = WaveformPeaks.Compute(samples, 2, 1);

        Assert.Equal([0.25f, 0f], peaks);
    }

    [Fact]
    public void Compute_FractionalBucketSize_CountIsCeilingOfDurationTimesRate()
    {
        // 22050 / 100 = 220.5 samples per bucket.
        var samples = new float[22050 * 3 + 7];

        var peaks = WaveformPeaks.Compute(samples, 22050, 100);

        Assert.Equal((int)Math.Ceiling(samples.Length * 100.0 / 22050), peaks.Length);
    }

    [Fact]
    public void Compute_FractionalBucketSize_SampleLandsInFloorBucket()
    {
        var samples = new float[2205];
        samples[221] = 0.8f; // floor(221 * 100 / 22050) = 1

        var peaks = WaveformPeaks.Compute(samples, 22050, 100);

        Assert.Equal(0f, peaks[0]);
        Assert.Equal(0.8f, peaks[1]);
        Assert.Equal(0f, peaks[2]);
    }

    [Fact]
    public void Compute_SingleSample_OneBucket()
    {
        Assert.Equal([0.75f], WaveformPeaks.Compute([-0.75f], 44100, 100));
    }

    [Fact]
    public void Compute_PeaksPerSecondEqualsSampleRate_OneBucketPerSample()
    {
        float[] samples = [0.1f, -0.2f, 0.3f];

        Assert.Equal([0.1f, 0.2f, 0.3f], WaveformPeaks.Compute(samples, 3, 3));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(44100, 0)]
    [InlineData(44100, -5)]
    [InlineData(50, 100)]
    public void Compute_InvalidRates_Throws(int sampleRate, int peaksPerSecond)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WaveformPeaks.Compute(new float[10], sampleRate, peaksPerSecond));
    }

    [Fact]
    public void Accumulator_ChunkedInput_MatchesSinglePass()
    {
        var signal = TestSignals.ClickTrack(120, 5, 22050, noiseAmplitude: 0.1f);
        var accumulator = new WaveformPeakAccumulator(22050, 100);
        for (var offset = 0; offset < signal.Length; offset += 777)
        {
            accumulator.Add(signal.AsSpan(offset, Math.Min(777, signal.Length - offset)));
        }

        Assert.Equal(WaveformPeaks.Compute(signal, 22050, 100), accumulator.ToArray());
        Assert.Equal(signal.Length, accumulator.SampleCount);
    }
}
