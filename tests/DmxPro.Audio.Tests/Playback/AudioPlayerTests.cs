using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

/// <summary>The player against a synthetic decoder and a fake (silent) device; the device clock is set by hand.</summary>
public class AudioPlayerTests
{
    private const int Rate = 44100;
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private static int Transition => (int)Math.Round(Rate * PlaybackSampleProcessor.TransitionSeconds);

    private static TimeSpan Frames(long frames) => AudioPlayer.FramesToTime(frames, Rate);

    private static async Task<(AudioPlayer Player, FakeAudioOutputFactory Outputs, FakeSampleDecoder Decoder)> LoadAsync(
        long lengthFrames = Rate * 10L)
    {
        var outputs = new FakeAudioOutputFactory();
        var decoder = new FakeSampleDecoder(Rate, 2, lengthFrames);
        var player = new AudioPlayer(outputs, (_, _) => decoder, null, Timeout.InfiniteTimeSpan);
        await player.LoadAsync("song.wav");
        return (player, outputs, decoder);
    }

    [Fact]
    public async Task LoadAsync_Song_ReportsDurationAndIsStopped()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();

        Assert.Equal("song.wav", player.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(10), player.Duration);
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.False(player.IsPlaying);
        Assert.False(outputs.Last.IsRunning);
    }

    [Fact]
    public async Task Seek_WhilePaused_PositionReflectsItImmediately()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();

        player.Seek(TimeSpan.FromSeconds(3.5));

        Assert.Equal(TimeSpan.FromSeconds(3.5), player.Position);
        Assert.False(player.IsPlaying);
        Assert.Equal(0, outputs.Last.PlayCount);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(60, 10)]
    public async Task Seek_OutOfRange_IsClamped(double seconds, double expected)
    {
        (AudioPlayer player, _, _) = await LoadAsync();

        player.Seek(TimeSpan.FromSeconds(seconds));

        Assert.Equal(TimeSpan.FromSeconds(expected), player.Position);
    }

    [Fact]
    public async Task Play_Position_IsWhatTheDeviceHasPlayedNotWhatWasDecoded()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        player.Play();
        FakeAudioOutput output = outputs.Last;

        output.Pull(4410); // 100 ms decoded and buffered
        output.HearAllBut(3528); // 80 ms still in the device buffer

        Assert.True(player.IsPlaying);
        Assert.Equal(Frames(882), player.Position);
    }

    [Fact]
    public async Task Play_AfterPausedSeek_StartsAtTheTarget()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        player.Seek(TimeSpan.FromSeconds(2));

        player.Play();
        outputs.Last.Pull(1000);
        outputs.Last.PlayedFrames = 500;

        Assert.Equal(TimeSpan.FromSeconds(2) + Frames(500), player.Position);
    }

    [Fact]
    public async Task Seek_WhilePlaying_HoldsTargetUntilTheNewAudioIsHeard()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        FakeAudioOutput output = outputs.Last;
        player.Play();
        output.Pull(8820);
        output.PlayedFrames = 4410;
        Assert.Equal(TimeSpan.FromSeconds(0.1), player.Position);

        player.Seek(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(5), player.Position);

        output.Pull(4410); // old audio fades out, then the new position fades in
        output.PlayedFrames = 8000; // still hearing pre-seek audio
        Assert.Equal(TimeSpan.FromSeconds(5), player.Position);

        output.PlayedFrames = 8820 + Transition + 100;
        Assert.Equal(TimeSpan.FromSeconds(5) + Frames(100), player.Position);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public async Task Position_DeviceClockWobblesBackwards_NeverDecreases()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        player.Play();
        outputs.Last.Pull(4410);

        outputs.Last.PlayedFrames = 1000;
        TimeSpan first = player.Position;
        outputs.Last.PlayedFrames = 990;
        TimeSpan second = player.Position;

        Assert.Equal(Frames(1000), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Pause_FreezesPosition_AndResumeContinuesFromThere()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, FakeSampleDecoder decoder) = await LoadAsync();
        FakeAudioOutput output = outputs.Last;
        player.Play();
        output.Pull(4410);
        output.PlayedFrames = 2000;

        player.Pause();
        output.Pull(4410); // fade-out + silence
        output.PlayedFrames = 4000; // the buffered tail is still being heard

        Assert.False(player.IsPlaying);
        Assert.Equal(Frames(2000), player.Position);

        player.Play();
        output.Pull(4410);
        output.PlayedFrames = 8820 + 300;

        Assert.Equal(Frames(2300), player.Position);
        Assert.Equal(1, decoder.SeekCount); // resume = exact seek back to the frozen position
        Assert.Equal(1, output.PlayCount); // the device kept running through the short pause
    }

    [Fact]
    public async Task Poll_PauseSilenceHeard_StopsDevice_AndPlayRestartsIt()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        FakeAudioOutput output = outputs.Last;
        player.Play();
        output.Pull(4410);
        output.PlayedFrames = 4410;
        player.Pause();
        output.Pull(4410);

        player.Poll();
        Assert.True(output.IsRunning); // silence not heard yet

        output.HearAllBut(0);
        player.Poll();
        Assert.False(output.IsRunning);
        Assert.Equal(1, output.StopCount);

        player.Play();
        output.Pull(1000);
        output.PlayedFrames = 100;
        Assert.Equal(2, output.PlayCount);
        Assert.Equal(Frames(4510), player.Position);
    }

    [Fact]
    public async Task Poll_WhilePlaying_KeepsDeviceRunning()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        player.Play();
        outputs.Last.Pull(4410);
        outputs.Last.HearAllBut(0);

        player.Poll();

        Assert.True(outputs.Last.IsRunning);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public async Task PlayToTheEnd_StopsPlayingSilentlyAtDuration()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync(lengthFrames: 1000);
        var errors = 0;
        player.Error += (_, _) => errors++;
        FakeAudioOutput output = outputs.Last;
        player.Play();
        output.Pull(3000);

        output.PlayedFrames = 999;
        Assert.True(player.IsPlaying);
        output.PlayedFrames = 1500;
        Assert.False(player.IsPlaying);
        Assert.Equal(player.Duration, player.Position);

        player.Play(); // at the end: nothing to hear
        Assert.False(player.IsPlaying);
        output.HearAllBut(0);
        player.Poll();
        Assert.False(output.IsRunning);
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task Stop_ReturnsToTheStart()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        player.Play();
        outputs.Last.Pull(4410);
        outputs.Last.PlayedFrames = 3000;

        player.Stop();

        Assert.False(player.IsPlaying);
        Assert.Equal(TimeSpan.Zero, player.Position);
    }

    [Fact]
    public async Task SetFadeOut_FadesSampleAccuratelyToSilence()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, FakeSampleDecoder decoder) = await LoadAsync();
        decoder.ValueAt = _ => 1f;
        player.SetFadeOut(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5));
        player.Play();

        float[] samples = outputs.Last.Pull(Rate + 1000);

        Assert.Equal(1f, samples[22050 * 2]);
        Assert.Equal(0.5f, samples[33075 * 2], 4);
        Assert.Equal(0f, samples[Rate * 2]);
        Assert.Equal(0f, samples[^1]);
    }

    [Fact]
    public async Task LoadAsync_NewSong_ClearsTheFadeOut()
    {
        var outputs = new FakeAudioOutputFactory();
        var player = new AudioPlayer(
            outputs, (_, _) => new FakeSampleDecoder(Rate, 2, Rate * 10L) { ValueAt = _ => 1f }, null, Timeout.InfiniteTimeSpan);
        await player.LoadAsync("a.wav");
        player.SetFadeOut(TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        await player.LoadAsync("b.wav");
        player.Play();
        float[] samples = outputs.Last.Pull(4410);

        Assert.Equal(1f, samples[^1]);
    }

    [Fact]
    public async Task Volume_IsClampedAndApplied()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, FakeSampleDecoder decoder) = await LoadAsync();
        decoder.ValueAt = _ => 1f;

        player.Volume = 3f;
        Assert.Equal(1f, player.Volume);
        player.Volume = 0.25f;
        player.Play();
        float[] samples = outputs.Last.Pull(4410);

        Assert.Equal(0.25f, samples[^1], 5);
    }

    [Fact]
    public async Task OutputFailed_RaisesError_StopsPlayback_AndPlayRecovers()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, _) = await LoadAsync();
        var raised = new TaskCompletionSource<AudioErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Error += (_, e) => raised.TrySetResult(e);
        FakeAudioOutput first = outputs.Last;
        player.Play();
        first.Pull(4410);
        first.PlayedFrames = 2000;

        first.RaiseFailed(new IOException("device removed"));
        AudioErrorEventArgs error = await raised.Task.WaitAsync(EventTimeout);

        Assert.Contains("device removed", error.Message);
        Assert.False(player.IsPlaying);
        Assert.True(first.IsDisposed);
        Assert.Equal(Frames(2000), player.Position);

        player.Play();
        Assert.Equal(2, outputs.Created.Count);
        Assert.True(outputs.Last.IsRunning);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public async Task ErrorHandler_DisposesPlayer_DoesNotDeadlock()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, FakeSampleDecoder decoder) = await LoadAsync();
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Error += (_, _) =>
        {
            player.Dispose();
            disposed.TrySetResult();
        };
        player.Play();

        outputs.Last.RaiseFailed(null);
        await disposed.Task.WaitAsync(EventTimeout);

        Assert.True(decoder.IsDisposed);
        Assert.Null(player.FilePath);
    }

    [Fact]
    public async Task DecodeFailure_RaisesError_AndStopsPlaying()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, FakeSampleDecoder decoder) = await LoadAsync();
        var raised = new TaskCompletionSource<AudioErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Error += (_, e) => raised.TrySetResult(e);
        decoder.FailAtFrame = 1000;
        player.Play();

        outputs.Last.Pull(4410);
        AudioErrorEventArgs error = await raised.Task.WaitAsync(EventTimeout);

        Assert.StartsWith("This file cannot be played", error.Message);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public async Task LoadAsync_NoAudioDevice_ThrowsAudioException()
    {
        var outputs = new FakeAudioOutputFactory { CreateError = new AudioException("No audio output device is available: none") };
        var decoder = new FakeSampleDecoder(Rate, 2, Rate);
        var player = new AudioPlayer(outputs, (_, _) => decoder, null, Timeout.InfiniteTimeSpan);

        AudioException error = await Assert.ThrowsAsync<AudioException>(() => player.LoadAsync("song.mp3"));

        Assert.StartsWith("No audio output device", error.Message);
        Assert.Null(player.FilePath);
        Assert.True(decoder.IsDisposed);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedType_ThrowsAudioException()
    {
        var player = new AudioPlayer(new FakeAudioOutputFactory(), (_, _) => throw new InvalidOperationException(), null, Timeout.InfiniteTimeSpan);

        AudioException error = await Assert.ThrowsAsync<AudioException>(() => player.LoadAsync("song.ogg"));

        Assert.StartsWith("This file cannot be played", error.Message);
    }

    [Fact]
    public async Task LoadAsync_Cancelled_Throws()
    {
        var player = new AudioPlayer(
            new FakeAudioOutputFactory(), (_, _) => new FakeSampleDecoder(Rate, 2, Rate), null, Timeout.InfiniteTimeSpan);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.LoadAsync("song.wav", new CancellationToken(true)));
        Assert.Null(player.FilePath);
    }

    [Fact]
    public async Task LoadAsync_SecondSong_ReplacesAndReleasesTheFirst()
    {
        var outputs = new FakeAudioOutputFactory();
        var decoders = new List<FakeSampleDecoder>();
        var player = new AudioPlayer(
            outputs,
            (_, _) =>
            {
                var d = new FakeSampleDecoder(Rate, 2, Rate * (decoders.Count + 1L));
                decoders.Add(d);
                return d;
            },
            null,
            Timeout.InfiniteTimeSpan);
        await player.LoadAsync("one.mp3");
        player.Play();
        outputs.Last.Pull(1000);

        await player.LoadAsync("two.flac");

        Assert.Equal("two.flac", player.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(2), player.Duration);
        Assert.False(player.IsPlaying);
        Assert.True(decoders[0].IsDisposed);
        Assert.True(outputs.Created[0].IsDisposed);
        Assert.False(decoders[1].IsDisposed);
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndReleasesDeviceAndDecoder()
    {
        (AudioPlayer player, FakeAudioOutputFactory outputs, FakeSampleDecoder decoder) = await LoadAsync();
        player.Play();

        player.Dispose();
        player.Dispose();
        player.Play();
        player.Seek(TimeSpan.FromSeconds(1));

        Assert.True(outputs.Last.IsDisposed);
        Assert.True(decoder.IsDisposed);
        Assert.Equal(TimeSpan.Zero, player.Position);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => player.LoadAsync("song.wav"));
    }

    [Fact]
    public void TimeConversions_RoundTripExactly()
    {
        Assert.Equal(44100, AudioPlayer.TimeToFrames(TimeSpan.FromSeconds(1), Rate));
        Assert.Equal(TimeSpan.FromSeconds(1), AudioPlayer.FramesToTime(44100, Rate));
        Assert.Equal(0, AudioPlayer.TimeToFrames(TimeSpan.FromSeconds(-1), Rate));
        Assert.Equal(12345, AudioPlayer.TimeToFrames(AudioPlayer.FramesToTime(12345, Rate), Rate));
    }
}
