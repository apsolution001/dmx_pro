using DmxPro.Audio.Analysis;

namespace DmxPro.Audio.Tests.Analysis;

public class BeatDetectorTests
{
    private const int Rate = 11025;
    private const double BpmTolerance = 0.5;
    private const double PhaseTolerance = 0.020;

    [Theory]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(128)]
    [InlineData(140)]
    [InlineData(174)]
    public void Detect_ClickTrack_FindsTempoAndPhase(double bpm)
    {
        var signal = TestSignals.ClickTrack(bpm, 30, Rate);

        var (detected, first) = BeatDetector.Detect(signal, Rate);

        Assert.NotNull(detected);
        Assert.InRange(detected.Value, bpm - BpmTolerance, bpm + BpmTolerance);
        Assert.InRange(first, -PhaseTolerance, PhaseTolerance);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(128)]
    [InlineData(140)]
    [InlineData(174)]
    public void Detect_ClickTrackWithNoise_FindsTempoAndPhase(double bpm)
    {
        var signal = TestSignals.ClickTrack(bpm, 30, Rate, noiseAmplitude: 0.2f, seed: (int)bpm);

        var (detected, first) = BeatDetector.Detect(signal, Rate);

        Assert.NotNull(detected);
        Assert.InRange(detected.Value, bpm - BpmTolerance, bpm + BpmTolerance);
        Assert.InRange(first, -PhaseTolerance, PhaseTolerance);
    }

    [Theory]
    [InlineData(90, 0f)]
    [InlineData(128, 0f)]
    [InlineData(174, 0f)]
    [InlineData(120, 0.1f)]
    public void Detect_SilentIntro_FirstBeatAfterIntro(double bpm, float noise)
    {
        var signal = TestSignals.ClickTrack(bpm, 30, Rate, introSeconds: 1.3, noiseAmplitude: noise);

        var (detected, first) = BeatDetector.Detect(signal, Rate);

        Assert.NotNull(detected);
        Assert.InRange(detected.Value, bpm - BpmTolerance, bpm + BpmTolerance);
        Assert.InRange(first, 1.3 - PhaseTolerance, 1.3 + PhaseTolerance);
    }

    [Fact]
    public void Detect_OffsetGrid_FindsPhaseOfFirstClick()
    {
        var signal = TestSignals.ClickTrack(128, 20, Rate, introSeconds: 0.2);

        var (detected, first) = BeatDetector.Detect(signal, Rate);

        Assert.NotNull(detected);
        Assert.InRange(first, 0.2 - PhaseTolerance, 0.2 + PhaseTolerance);
    }

    [Theory]
    [InlineData(22050)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void Detect_OtherSampleRates_FindsTempo(int sampleRate)
    {
        var signal = TestSignals.ClickTrack(128, 20, sampleRate, introSeconds: 0.5);

        var (detected, first) = BeatDetector.Detect(signal, sampleRate);

        Assert.NotNull(detected);
        Assert.InRange(detected.Value, 128 - BpmTolerance, 128 + BpmTolerance);
        Assert.InRange(first, 0.5 - PhaseTolerance, 0.5 + PhaseTolerance);
    }

    [Fact]
    public void Detect_NonIntegerTempo_RoundsToTenth()
    {
        var signal = TestSignals.ClickTrack(123.4, 40, Rate);

        var (detected, _) = BeatDetector.Detect(signal, Rate);

        Assert.NotNull(detected);
        Assert.InRange(detected.Value, 123.3, 123.5);
        Assert.Equal(Math.Round(detected.Value, 1), detected.Value);
    }

    [Fact]
    public void Detect_Silence_ReturnsNull()
    {
        var (bpm, first) = BeatDetector.Detect(new float[Rate * 20], Rate);

        Assert.Null(bpm);
        Assert.Equal(0, first);
    }

    [Fact]
    public void Detect_VeryQuietClicks_ReturnsNull()
    {
        var signal = TestSignals.ClickTrack(120, 20, Rate);
        for (var i = 0; i < signal.Length; i++)
        {
            signal[i] *= 0.0005f;
        }

        Assert.Null(BeatDetector.Detect(signal, Rate).Bpm);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(23)]
    public void Detect_WhiteNoise_ReturnsNull(int seed)
    {
        var (bpm, first) = BeatDetector.Detect(TestSignals.WhiteNoise(30, Rate, seed: seed), Rate);

        Assert.Null(bpm);
        Assert.Equal(0, first);
    }

    [Fact]
    public void Detect_SteadyTone_ReturnsNull()
    {
        Assert.Null(BeatDetector.Detect(TestSignals.Sine(440, 20, Rate), Rate).Bpm);
    }

    [Fact]
    public void Detect_TooShort_ReturnsNull()
    {
        var signal = TestSignals.ClickTrack(120, 3.5, Rate);

        Assert.Null(BeatDetector.Detect(signal, Rate).Bpm);
    }

    [Fact]
    public void Detect_Empty_ReturnsNull()
    {
        Assert.Null(BeatDetector.Detect(ReadOnlySpan<float>.Empty, Rate).Bpm);
    }

    [Fact]
    public void Detect_SameInput_IsDeterministic()
    {
        var signal = TestSignals.ClickTrack(128, 20, Rate, noiseAmplitude: 0.2f);

        Assert.Equal(BeatDetector.Detect(signal, Rate), BeatDetector.Detect(signal, Rate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Detect_NonPositiveSampleRate_Throws(int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatDetector.Detect(new float[10], sampleRate));
    }

    [Fact]
    public void DetectFromEnvelope_StreamedInChunks_MatchesSinglePass()
    {
        var signal = TestSignals.ClickTrack(140, 20, Rate, introSeconds: 0.7, noiseAmplitude: 0.1f);
        var envelope = new OnsetEnvelope(Rate);
        for (var offset = 0; offset < signal.Length; offset += 1000)
        {
            envelope.Add(signal.AsSpan(offset, Math.Min(1000, signal.Length - offset)));
        }

        Assert.Equal(BeatDetector.Detect(signal, Rate), BeatDetector.Detect(envelope));
        Assert.Equal(
            BeatDetector.Detect(envelope),
            BeatDetector.DetectFromEnvelope(envelope.Values, envelope.FrameRate, envelope.FrameOffsetSeconds));
    }

    [Fact]
    public void DetectFromEnvelope_NonPositiveFrameRate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatDetector.DetectFromEnvelope(new float[10], 0));
    }

    [Theory]
    [InlineData(90, 1.0)]
    [InlineData(120, 1.0)]
    [InlineData(140, 1.0)]
    public void TempoWeight_PreferredRange_IsOne(double bpm, double expected)
    {
        Assert.Equal(expected, BeatDetector.TempoWeight(bpm));
    }

    [Fact]
    public void TempoWeight_OutsidePreferredRange_FallsOff()
    {
        Assert.True(BeatDetector.TempoWeight(64) < 1);
        Assert.True(BeatDetector.TempoWeight(200) < BeatDetector.TempoWeight(170));
        Assert.True(BeatDetector.TempoWeight(60) < BeatDetector.TempoWeight(80));
    }
}
