using DmxPro.Audio.Analysis;

namespace DmxPro.Audio.Tests.Analysis;

/// <summary>Beat detection, onset envelope and waveform edge cases (tempo range, DC, clipping, odd rates, chunking).</summary>
public class AnalysisEdgeCaseTests
{
    private const int Rate = 11025;
    private const double BpmTolerance = 0.5;

    [Theory]
    [InlineData(62)]
    [InlineData(75)]
    [InlineData(105)]
    [InlineData(160)]
    [InlineData(195)]
    public void Detect_ClickTrackAcrossTheSearchRange_FindsTempo(double bpm)
    {
        var signal = TestSignals.ClickTrack(bpm, 40, Rate);

        var (detected, _) = BeatDetector.Detect(signal, Rate);

        Assert.NotNull(detected);
        Assert.InRange(detected.Value, bpm - BpmTolerance, bpm + BpmTolerance);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(-0.4f)]
    public void Detect_DcOffset_SameTempoAndPhase(float dc)
    {
        var clean = TestSignals.ClickTrack(128, 30, Rate);
        var shifted = clean.Select(s => s + dc).ToArray();

        var (bpmClean, firstClean) = BeatDetector.Detect(clean, Rate);
        var (bpmShifted, firstShifted) = BeatDetector.Detect(shifted, Rate);

        Assert.Equal(bpmClean, bpmShifted);
        Assert.InRange(firstShifted, firstClean - 0.01, firstClean + 0.01);
    }

    [Fact]
    public void Detect_HardClipped_StillFindsTempo()
    {
        var clipped = TestSignals.ClickTrack(120, 30, Rate, noiseAmplitude: 0.02f).Select(s => Math.Clamp(s * 8f, -1f, 1f)).ToArray();

        var (bpm, first) = BeatDetector.Detect(clipped, Rate);

        Assert.NotNull(bpm);
        Assert.InRange(bpm.Value, 120 - BpmTolerance, 120 + BpmTolerance);
        Assert.InRange(first, -0.02, 0.02);
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(96000)]
    public void Detect_UnusualSampleRates_FindsTempo(int sampleRate)
    {
        var signal = TestSignals.ClickTrack(120, 20, sampleRate);

        var (bpm, _) = BeatDetector.Detect(signal, sampleRate);

        Assert.NotNull(bpm);
        Assert.InRange(bpm.Value, 120 - BpmTolerance, 120 + BpmTolerance);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(400)]
    public void Detect_TinySampleRates_DoesNotThrow(int sampleRate)
    {
        var signal = TestSignals.WhiteNoise(30, sampleRate);

        var exception = Record.Exception(() => BeatDetector.Detect(signal, sampleRate));

        Assert.Null(exception);
    }

    [Fact]
    public void Detect_LongIntroThenBeat_FirstBeatAfterIntroAndTempoKept()
    {
        var signal = TestSignals.ClickTrack(100, 90, Rate, introSeconds: 45);

        var (bpm, first) = BeatDetector.Detect(signal, Rate);

        Assert.NotNull(bpm);
        Assert.InRange(bpm.Value, 100 - BpmTolerance, 100 + BpmTolerance);
        Assert.InRange(first, 45 - 0.02, 45 + 0.02);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(441)]
    [InlineData(4096)]
    public void OnsetEnvelope_AnyChunkSize_IdenticalToSinglePass(int chunk)
    {
        var signal = TestSignals.ClickTrack(128, 8, Rate, noiseAmplitude: 0.05f);
        var single = new OnsetEnvelope(Rate);
        single.Add(signal);

        var chunked = new OnsetEnvelope(Rate);
        for (var i = 0; i < signal.Length; i += chunk)
        {
            chunked.Add(signal.AsSpan(i, Math.Min(chunk, signal.Length - i)));
        }

        Assert.Equal(single.Values.ToArray(), chunked.Values.ToArray());
        Assert.Equal(single.PeakAmplitude, chunked.PeakAmplitude);
        Assert.Equal(single.DurationSeconds, chunked.DurationSeconds);
    }

    [Fact]
    public void WaveformPeaks_FractionalBuckets_ChunkedEqualsSinglePassForAwkwardChunks()
    {
        var signal = TestSignals.WhiteNoise(3.3, 22051);
        var single = WaveformPeaks.Compute(signal, 22051, 100);

        var accumulator = new WaveformPeakAccumulator(22051, 100);
        var random = new Random(3);
        for (var i = 0; i < signal.Length;)
        {
            var n = Math.Min(random.Next(1, 700), signal.Length - i);
            accumulator.Add(signal.AsSpan(i, n));
            i += n;
        }

        Assert.Equal(single, accumulator.ToArray());
        Assert.Equal((int)Math.Ceiling(signal.Length * 100.0 / 22051), single.Length);
    }
}
