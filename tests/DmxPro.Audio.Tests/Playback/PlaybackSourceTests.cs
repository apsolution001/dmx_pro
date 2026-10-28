using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

public class PlaybackSourceTests
{
    private const int Rate = 44100;
    private const int Channels = 2;

    private static (PlaybackSource Source, FakeSampleDecoder Decoder) Create(long length = Rate * 10L)
    {
        var decoder = new FakeSampleDecoder(Rate, Channels, length) { ValueAt = _ => 1f };
        return (new PlaybackSource(decoder), decoder);
    }

    private static float[] Read(PlaybackSource source, int frames)
    {
        var buffer = new float[frames * Channels];
        Assert.Equal(buffer.Length, source.Read(buffer, 0, buffer.Length));
        return buffer;
    }

    private static HeardFrame Map(PlaybackSource source, long outputFrame)
    {
        Assert.True(source.TryMapOutputFrame(outputFrame, out HeardFrame heard));
        return heard;
    }

    [Fact]
    public void Read_BeforeRun_IsSilent()
    {
        (PlaybackSource source, _) = Create();

        float[] samples = Read(source, 1000);

        Assert.All(samples, s => Assert.Equal(0f, s));
        Assert.Equal(0, source.SilentSinceOutputFrame);
    }

    [Fact]
    public void Read_AfterRun_FadesInAndMapsSongFrames()
    {
        (PlaybackSource source, FakeSampleDecoder decoder) = Create();
        source.Run(1000, 1);

        float[] samples = Read(source, 2000);

        Assert.Equal(1, decoder.SeekCount);
        Assert.Equal(0f, samples[0]);
        Assert.Equal(1f, samples[^1]);
        Assert.Equal(new HeardFrame(1000, 1, OutputSpanKind.Audio), Map(source, 0));
        Assert.Equal(new HeardFrame(2999, 1, OutputSpanKind.Audio), Map(source, 1999));
        Assert.Equal(-1, source.SilentSinceOutputFrame);
    }

    [Fact]
    public void Read_RunFromCurrentFrame_DoesNotSeek()
    {
        (PlaybackSource source, FakeSampleDecoder decoder) = Create();
        source.Run(0, 1);

        Read(source, 100);

        Assert.Equal(0, decoder.SeekCount);
    }

    [Fact]
    public void Read_SeekWhilePlaying_FadesOutOldAudioThenFadesInAtTarget()
    {
        (PlaybackSource source, FakeSampleDecoder decoder) = Create();
        source.Run(0, 1);
        Read(source, 5000);

        source.Run(100_000, 2);
        float[] samples = Read(source, 2000);

        int transition = (int)Math.Round(Rate * PlaybackSampleProcessor.TransitionSeconds);
        // Old audio continues for the fade-out (frames 5000..), then silence-to-full fade-in at the new position.
        Assert.Equal(new HeardFrame(5000, 1, OutputSpanKind.Audio), Map(source, 5000));
        Assert.Equal(new HeardFrame(5000 + transition - 1, 1, OutputSpanKind.Audio), Map(source, 5000 + transition - 1));
        Assert.Equal(new HeardFrame(100_000, 2, OutputSpanKind.Audio), Map(source, 5000 + transition));
        Assert.Equal(1f, samples[0]);
        Assert.Equal(0f, samples[transition * Channels]);
        Assert.Equal(1f, samples[^1]);

        // No step anywhere is larger than one transition step.
        float maxStep = 1f / transition + 1e-5f;
        for (int i = Channels; i < samples.Length; i += Channels)
        {
            Assert.True(Math.Abs(samples[i] - samples[i - Channels]) <= maxStep);
        }

        Assert.Equal(1, decoder.SeekCount); // only the jump seeks; the first Run started at the decoder's frame
    }

    [Fact]
    public void Read_Halt_FadesOutThenHoldsInSilence()
    {
        (PlaybackSource source, _) = Create();
        source.Run(0, 1);
        Read(source, 1000);

        source.Halt();
        float[] samples = Read(source, 1000);

        int transition = (int)Math.Round(Rate * PlaybackSampleProcessor.TransitionSeconds);
        Assert.Equal(1f, samples[0]);
        Assert.All(samples[(transition * Channels)..], s => Assert.Equal(0f, s));
        Assert.Equal(1000 + transition, source.SilentSinceOutputFrame);
        Assert.Equal(new HeardFrame(1000 + transition, 1, OutputSpanKind.Halted), Map(source, 1900));
    }

    [Fact]
    public void Read_PastEndOfSong_RecordsEndAndOutputsSilence()
    {
        (PlaybackSource source, _) = Create(length: 1000);
        source.Run(0, 1);

        float[] samples = Read(source, 1500);

        Assert.All(samples[(1000 * Channels)..], s => Assert.Equal(0f, s));
        Assert.Equal(new HeardFrame(1000, 1, OutputSpanKind.End), Map(source, 1200));
        Assert.Equal(1000, source.SilentSinceOutputFrame);
    }

    [Fact]
    public void Read_RunAfterEnd_SeeksAndPlaysAgain()
    {
        (PlaybackSource source, _) = Create(length: 1000);
        source.Run(0, 1);
        Read(source, 1500);

        source.Run(200, 2);
        Read(source, 100);

        Assert.Equal(new HeardFrame(200, 2, OutputSpanKind.Audio), Map(source, 1500));
    }

    [Fact]
    public void Read_DecoderThrows_OutputsSilenceAndReportsOnce()
    {
        (PlaybackSource source, FakeSampleDecoder decoder) = Create();
        decoder.FailAtFrame = 500;
        var errors = new List<Exception>();
        source.DecodeFailed = errors.Add;
        source.Run(0, 1);

        float[] samples = Read(source, 2000);
        Read(source, 2000);

        Assert.Single(errors);
        Assert.All(samples[(500 * Channels)..], s => Assert.Equal(0f, s));
        Assert.Equal(OutputSpanKind.End, Map(source, 1000).Kind);
    }

    [Fact]
    public void Read_AfterDispose_ReturnsSilenceWithoutTouchingDecoder()
    {
        (PlaybackSource source, FakeSampleDecoder decoder) = Create();
        source.Run(0, 1);
        source.Dispose();

        float[] samples = Read(source, 100);

        Assert.True(decoder.IsDisposed);
        Assert.All(samples, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Read_SilenceImmediately_NextRunStartsWithoutOldAudio()
    {
        (PlaybackSource source, _) = Create();
        source.Run(0, 1);
        Read(source, 1000);

        source.SilenceImmediately();
        source.Run(5000, 2);
        float[] samples = Read(source, 10);

        Assert.Equal(0f, samples[0]);
        Assert.Equal(new HeardFrame(5000, 2, OutputSpanKind.Audio), Map(source, 1000));
    }

    [Fact]
    public void Read_SteadyState_AllocatesNothing()
    {
        (PlaybackSource source, _) = Create(length: Rate * 600L);
        var buffer = new float[441 * Channels];
        source.Run(0, 1);
        source.SetVolume(0.5f);
        source.SetFadeOut(Rate * 500L, Rate);
        long allocated = AllocationProbe.Measure(i =>
        {
            source.Read(buffer, 0, buffer.Length);
            if (i % 500 == 250)
            {
                source.Run(Rate * 100L + i, 2); // a seek mid-run allocates nothing either
            }
        });
        Assert.Equal(0, allocated);
    }
}
