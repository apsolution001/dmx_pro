using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

/// <summary>
/// Transport commands must never be lost, whatever size the device reads in: a command queued while the fade-out of the
/// old audio uses up a whole read must still execute on a later read (regression: it was taken and dropped).
/// </summary>
public class PlaybackSourceCommandTests
{
    private const int Rate = 44100;
    private const int Channels = 2;

    private static int Transition => (int)Math.Round(Rate * PlaybackSampleProcessor.TransitionSeconds);

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

    [Fact]
    public void Read_SeekWhenFadeOutFillsTheWholeRead_SeekStillExecutes()
    {
        (PlaybackSource source, FakeSampleDecoder decoder) = Create();
        source.Run(0, 1);
        Read(source, 5000);

        source.Run(100_000, 2);
        Read(source, Transition); // the device happens to ask for exactly the fade-out length
        float[] after = Read(source, 2000);

        Assert.True(source.TryMapOutputFrame(5000 + Transition + 1000, out HeardFrame heard));
        Assert.Equal(2, heard.Generation);
        Assert.Equal(1, decoder.SeekCount);
        Assert.Equal(1f, after[^1]);
    }

    [Fact]
    public void Read_SeekWithSmallDeviceReads_SeekStillExecutes()
    {
        (PlaybackSource source, _) = Create();
        source.Run(0, 1);
        Read(source, 5000);

        source.Run(100_000, 2);
        float[] last = [];
        for (int i = 0; i < 40; i++)
        {
            last = Read(source, 128); // small device period (e.g. a low-latency WASAPI / resampler pull)
        }

        Assert.True(source.TryMapOutputFrame(5000 + (40 * 128) - 1, out HeardFrame heard));
        Assert.Equal(2, heard.Generation);
        Assert.True(heard.SourceFrame >= 100_000);
        Assert.Equal(1f, last[^1]); // music is audible again
    }

    [Fact]
    public void Read_PauseWithSmallDeviceReads_GoesSilentAndReportsSilence()
    {
        (PlaybackSource source, FakeSampleDecoder decoder) = Create();
        source.Run(0, 1);
        Read(source, 5000);

        source.Halt();
        for (int i = 0; i < 40; i++)
        {
            Read(source, 128);
        }

        long decodedAfterFade = decoder.NextFrame;
        Read(source, 4410);

        // Halted: the song position holds and the trailing silence is visible, so the player can stop the device.
        Assert.True(source.SilentSinceOutputFrame >= 0);
        Assert.Equal(decodedAfterFade, decoder.NextFrame);
    }

    [Fact]
    public async Task Seek_WhilePlayingWithSmallDeviceReads_NewPositionIsHeard()
    {
        var outputs = new FakeAudioOutputFactory();
        var decoder = new FakeSampleDecoder(Rate, Channels, Rate * 10L);
        using var player = new AudioPlayer(outputs, (_, _) => decoder, null, Timeout.InfiniteTimeSpan);
        await player.LoadAsync("song.wav");
        FakeAudioOutput output = outputs.Last;
        player.Play();
        output.Pull(8820);
        output.PlayedFrames = 4410;

        player.Seek(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 100; i++)
        {
            output.Pull(Transition); // device period equal to the 8 ms transition
        }

        output.HearAllBut(0);

        // 100 reads after the seek the new audio must be heard and the position must have moved on from the target.
        Assert.True(player.Position > TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pause_WithSmallDeviceReads_PollStopsTheDevice()
    {
        var outputs = new FakeAudioOutputFactory();
        var decoder = new FakeSampleDecoder(Rate, Channels, Rate * 10L);
        using var player = new AudioPlayer(outputs, (_, _) => decoder, null, Timeout.InfiniteTimeSpan);
        await player.LoadAsync("song.wav");
        FakeAudioOutput output = outputs.Last;
        player.Play();
        output.Pull(4410);
        output.HearAllBut(0);

        player.Pause();
        for (int i = 0; i < 40; i++)
        {
            output.Pull(256);
        }

        output.HearAllBut(0);
        player.Poll();

        Assert.False(output.IsRunning);
    }
}
