using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

public class PlaybackSampleProcessorTests
{
    private const int Rate = 48000;
    private const int Channels = 2;

    private static float[] Ones(int frames) => Enumerable.Repeat(1f, frames * Channels).ToArray();

    /// <summary>Processes <paramref name="frames"/> frames of 1.0 in chunks and returns the gain of every frame.</summary>
    private static float[] Gains(PlaybackSampleProcessor processor, long firstFrame, int frames, int chunk = 37)
    {
        float[] samples = Ones(frames);
        for (int done = 0; done < frames; done += chunk)
        {
            int n = Math.Min(chunk, frames - done);
            processor.Process(samples.AsSpan(done * Channels, n * Channels), firstFrame + done);
        }

        var gains = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            Assert.Equal(samples[f * Channels], samples[(f * Channels) + 1]); // all channels get the same gain
            gains[f] = samples[f * Channels];
        }

        return gains;
    }

    [Fact]
    public void Process_VolumeChange_RampHasNoStepAboveTolerance()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        processor.TargetVolume = 0f;
        float[] down = Gains(processor, 0, 2000);
        processor.TargetVolume = 0.8f;
        float[] up = Gains(processor, 2000, 2000);

        float maxStep = 1f / processor.VolumeRampFrames + 1e-6f;
        float previous = 1f;
        foreach (float gain in down.Concat(up))
        {
            Assert.True(Math.Abs(gain - previous) <= maxStep, $"step {Math.Abs(gain - previous)} > {maxStep}");
            previous = gain;
        }

        Assert.Equal(0f, down[^1]);
        Assert.Equal(0.8f, up[^1], 5);
        Assert.Equal(960, processor.VolumeRampFrames); // 20 ms at 48 kHz
    }

    [Fact]
    public void Process_VolumeRamp_ReachesTargetWithinRampTime()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels) { TargetVolume = 0f };
        float[] gains = Gains(processor, 0, processor.VolumeRampFrames + 1);

        Assert.True(gains[processor.VolumeRampFrames / 2] > 0.4f);
        Assert.Equal(0f, gains[processor.VolumeRampFrames]);
    }

    [Fact]
    public void Process_FadeOutWindow_StartsAtExactFrameAndIsLinear()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        processor.SetFadeOut(1000, 400);

        float[] gains = Gains(processor, 0, 2000);

        for (int f = 0; f <= 1000; f++)
        {
            Assert.Equal(1f, gains[f]);
        }

        for (int k = 1; k < 400; k++)
        {
            Assert.Equal(1f - (k / 400f), gains[1000 + k], 5);
        }

        Assert.True(gains[1001] < 1f);
    }

    [Fact]
    public void Process_AfterFadeOutWindow_IsSilent()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        processor.SetFadeOut(1000, 400);

        float[] gains = Gains(processor, 1400, 5000);

        Assert.All(gains, g => Assert.Equal(0f, g));
    }

    [Fact]
    public void Process_FadeWindow_UsesSongFrameNotReadOffset()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        processor.SetFadeOut(10_000, 1000);

        // A read that starts mid-fade (e.g. right after a seek into the fade) gets the gain for its song frames.
        float[] gains = Gains(processor, 10_500, 10, chunk: 3);

        Assert.Equal(0.5f, gains[0], 5);
        Assert.Equal(1f - (509 / 1000f), gains[9], 5);
    }

    [Fact]
    public void SetFadeOut_ZeroLength_ClearsFade()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        processor.SetFadeOut(0, 100);
        processor.SetFadeOut(0, 0);

        float[] gains = Gains(processor, 0, 500);

        Assert.False(processor.HasFadeOut);
        Assert.All(gains, g => Assert.Equal(1f, g));
    }

    [Fact]
    public void BeginFadeIn_AfterSeek_RampsFromSilenceOverTransitionFrames()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        int n = processor.TransitionFrames;

        processor.BeginFadeIn();
        float[] gains = Gains(processor, 123_456, n + 10);

        Assert.Equal(384, n); // 8 ms at 48 kHz
        for (int k = 0; k < n; k++)
        {
            Assert.Equal((float)k / n, gains[k], 5);
        }

        Assert.All(gains[n..], g => Assert.Equal(1f, g));
    }

    [Fact]
    public void BeginFadeOut_ReachesSilenceAfterFramesUntilSilent()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        int n = processor.FramesUntilSilent;

        processor.BeginFadeOut();
        float[] gains = Gains(processor, 0, n + 50);

        Assert.Equal(processor.TransitionFrames, n);
        Assert.Equal(1f, gains[0]);
        for (int k = 1; k < n; k++)
        {
            Assert.True(gains[k] < gains[k - 1]);
        }

        Assert.All(gains[n..], g => Assert.Equal(0f, g));
        Assert.True(processor.IsTransitionSilent);
    }

    [Fact]
    public void BeginFadeOut_DuringFadeIn_ReversesWithoutJump()
    {
        var processor = new PlaybackSampleProcessor(Rate, Channels);
        processor.BeginFadeIn();
        float[] up = Gains(processor, 0, 100);
        processor.BeginFadeOut();
        float[] down = Gains(processor, 100, 200);

        float step = 1f / processor.TransitionFrames + 1e-6f;
        Assert.True(Math.Abs(down[0] - up[^1]) <= step);
        Assert.Equal(0f, down[^1]);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(0.25f, 0.25f)]
    public void ClampVolume_OutOfRange_IsClamped(float input, float expected) =>
        Assert.Equal(expected, PlaybackSampleProcessor.ClampVolume(input));
}
