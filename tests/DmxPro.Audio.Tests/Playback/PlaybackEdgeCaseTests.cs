using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

/// <summary>Playback edge cases: in-memory decoding, fade window, rapid transport, load races, memory reporting.</summary>
public class PlaybackEdgeCaseTests
{
    private const int Rate = 44100;

    private static int Transition => (int)Math.Round(Rate * PlaybackSampleProcessor.TransitionSeconds);

    private static TimeSpan Frames(long frames) => AudioPlayer.FramesToTime(frames, Rate);

    [Fact]
    public void MemorySampleDecoder_SeekOutOfBoundsAndChunkBoundary_ClampsAndReadsExactSamples()
    {
        // 600k stereo frames = 1.2M samples: crosses the 2^20-sample chunk boundary.
        const long length = 600_000;
        var source = new FakeSampleDecoder(Rate, 2, length) { ChunkFrames = 8192, ValueAt = f => (f % 1000) / 1000f };
        using var decoder = MemorySampleDecoder.DecodeAll(source, long.MaxValue, CancellationToken.None)!;

        decoder.Seek(-10);
        Assert.Equal(0, decoder.NextFrame);
        decoder.Seek(length + 10);
        Assert.Equal(length, decoder.NextFrame);
        Assert.Equal(0, decoder.Read(new float[64], out _));

        long boundaryFrame = (1 << 20) / 2;
        decoder.Seek(boundaryFrame - 3);
        var buffer = new float[12];
        Assert.Equal(6, decoder.Read(buffer, out long first));
        Assert.Equal(boundaryFrame - 3, first);
        for (int i = 0; i < 6; i++)
        {
            float expected = MathF.Round(((first + i) % 1000) / 1000f * 32768f) / 32768f;
            Assert.Equal(expected, buffer[i * 2]);
            Assert.Equal(expected, buffer[(i * 2) + 1]);
        }
    }

    [Fact]
    public void MemorySampleDecoder_TooLong_ReturnsNull()
    {
        var source = new FakeSampleDecoder(Rate, 2, 10_000);

        Assert.Null(MemorySampleDecoder.DecodeAll(source, 10_000, CancellationToken.None));
    }

    [Fact]
    public void FadeWindowGain_Boundaries_AreExact()
    {
        var processor = new PlaybackSampleProcessor(Rate, 2);
        processor.SetFadeOut(1000, 100);

        Assert.Equal(1f, processor.FadeWindowGain(999));
        Assert.Equal(1f, processor.FadeWindowGain(1000));
        Assert.Equal(0.5f, processor.FadeWindowGain(1050));
        Assert.Equal(0f, processor.FadeWindowGain(1100));
        Assert.Equal(0f, processor.FadeWindowGain(long.MaxValue));
    }

    [Fact]
    public void FadeWindowGain_NegativeStart_SongStartIsAlreadyPartlyFaded()
    {
        var processor = new PlaybackSampleProcessor(Rate, 2);
        processor.SetFadeOut(-50, 100);

        Assert.Equal(0.5f, processor.FadeWindowGain(0));
        Assert.Equal(0.25f, processor.FadeWindowGain(25));
        Assert.Equal(0f, processor.FadeWindowGain(50));
    }

    [Fact]
    public async Task SetFadeOut_StartBeforeTheSong_GainFollowsTheUncutLine()
    {
        var outputs = new FakeAudioOutputFactory();
        var decoder = new FakeSampleDecoder(Rate, 2, Rate * 10L) { ValueAt = _ => 1f };
        using var player = new AudioPlayer(outputs, (_, _) => decoder, null, Timeout.InfiniteTimeSpan);
        await player.LoadAsync("song.wav");

        // The fade began 0.5 s before the song start and lasts 1 s: at song time 0.25 s three quarters are done.
        player.SetFadeOut(TimeSpan.FromSeconds(-0.5), TimeSpan.FromSeconds(1));
        player.Play();
        float[] samples = outputs.Last.Pull(Rate);

        Assert.Equal(0.25f, samples[(Rate / 4) * 2], 4);
        Assert.Equal(0f, samples[(Rate / 2) * 2]);
        Assert.Equal(0f, samples[^1]);
    }

    [Fact]
    public void TimeToSignedFrames_NegativeAndHugeTimes_KeepsSignAndStaysBounded()
    {
        Assert.Equal(-Rate / 2, AudioPlayer.TimeToSignedFrames(TimeSpan.FromSeconds(-0.5), Rate));
        Assert.Equal(Rate, AudioPlayer.TimeToSignedFrames(TimeSpan.FromSeconds(1), Rate));
        Assert.Equal(-(1L << 52), AudioPlayer.TimeToSignedFrames(TimeSpan.MinValue, 192_000));
        Assert.Equal(1L << 52, AudioPlayer.TimeToSignedFrames(TimeSpan.MaxValue, 192_000));
    }

    [Fact]
    public async Task MemoryBytes_SongDecodedIntoMemory_ReportsTheChunksHeld()
    {
        // 600k stereo frames = 1.2M samples: two 2^20-sample chunks of 16-bit samples.
        var outputs = new FakeAudioOutputFactory();
        using var player = new AudioPlayer(
            outputs,
            (_, ct) => MemorySampleDecoder.DecodeAll(new FakeSampleDecoder(Rate, 2, 600_000), long.MaxValue, ct)!,
            null,
            Timeout.InfiniteTimeSpan);

        Assert.Equal(0, player.MemoryBytes);
        await player.LoadAsync("song.wav");

        Assert.Equal(2L * (1 << 20) * sizeof(short), player.MemoryBytes);
    }

    [Fact]
    public async Task MemoryBytes_StreamedSong_IsZero()
    {
        var outputs = new FakeAudioOutputFactory();
        using var player = new AudioPlayer(outputs, (_, _) => new FakeSampleDecoder(Rate, 2, Rate * 10L), null, Timeout.InfiniteTimeSpan);
        await player.LoadAsync("song.wav");

        Assert.Equal(0, player.MemoryBytes);
    }

    [Fact]
    public async Task MemoryBytes_AfterDispose_IsZero()
    {
        var outputs = new FakeAudioOutputFactory();
        var player = new AudioPlayer(
            outputs,
            (_, ct) => MemorySampleDecoder.DecodeAll(new FakeSampleDecoder(Rate, 2, 10_000), long.MaxValue, ct)!,
            null,
            Timeout.InfiniteTimeSpan);
        await player.LoadAsync("song.wav");
        Assert.True(player.MemoryBytes > 0);

        player.Dispose();

        Assert.Equal(0, player.MemoryBytes);
    }

    [Fact]
    public async Task RapidPausePlay_BeforeAnyRead_ResumesAtThePausedPosition()
    {
        var outputs = new FakeAudioOutputFactory();
        var decoder = new FakeSampleDecoder(Rate, 2, Rate * 10L);
        using var player = new AudioPlayer(outputs, (_, _) => decoder, null, Timeout.InfiniteTimeSpan);
        await player.LoadAsync("song.wav");
        FakeAudioOutput output = outputs.Last;
        player.Play();
        output.Pull(4410);
        output.PlayedFrames = 2000;

        player.Pause();
        player.Play();
        player.Pause();
        player.Play();
        output.Pull(4410);
        output.PlayedFrames = 4410 + Transition + 100;

        Assert.True(player.IsPlaying);
        Assert.Equal(Frames(2100), player.Position);
    }

    [Fact]
    public async Task Dispose_DuringLoad_ReleasesTheNewDecoderAndDevice()
    {
        var outputs = new FakeAudioOutputFactory();
        var decoder = new FakeSampleDecoder(Rate, 2, Rate * 10L);
        using var gate = new ManualResetEventSlim();
        var player = new AudioPlayer(
            outputs,
            (_, _) =>
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                return decoder;
            },
            null,
            Timeout.InfiniteTimeSpan);

        Task load = player.LoadAsync("song.wav");
        player.Dispose();
        gate.Set();

        await Assert.ThrowsAnyAsync<Exception>(() => load);
        Assert.True(decoder.IsDisposed);
        Assert.All(outputs.Created, o => Assert.True(o.IsDisposed));
    }

    [Fact]
    public async Task LoadAsync_SecondLoadOvertakesFirst_FirstIsCancelledAndReleased()
    {
        var outputs = new FakeAudioOutputFactory();
        var slow = new FakeSampleDecoder(Rate, 2, Rate * 10L);
        var fast = new FakeSampleDecoder(Rate, 2, Rate * 20L);
        using var gate = new ManualResetEventSlim();
        using var player = new AudioPlayer(
            outputs,
            (path, _) =>
            {
                if (path == "slow.wav")
                {
                    gate.Wait(TimeSpan.FromSeconds(10));
                    return slow;
                }

                return fast;
            },
            null,
            Timeout.InfiniteTimeSpan);

        Task first = player.LoadAsync("slow.wav");
        await player.LoadAsync("fast.wav");
        gate.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal("fast.wav", player.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(20), player.Duration);
        Assert.True(slow.IsDisposed);
        Assert.False(fast.IsDisposed);
    }

    [Fact]
    public void HeardPositionMap_DeviceClockBackwardsAcrossSeek_MapsToTheOlderSpan()
    {
        var map = new HeardPositionMap(4);
        map.Record(1, OutputSpanKind.Audio, 0, 1000);
        map.Record(2, OutputSpanKind.Audio, 50_000, 1000);

        Assert.True(map.TryMap(999, out HeardFrame before));
        Assert.Equal(new HeardFrame(999, 1, OutputSpanKind.Audio), before);
        Assert.True(map.TryMap(1000, out HeardFrame after));
        Assert.Equal(new HeardFrame(50_000, 2, OutputSpanKind.Audio), after);
    }
}
