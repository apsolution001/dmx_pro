using DmxPro.Audio.Playback;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace DmxPro.Audio.Tests.Playback;

/// <summary>
/// Real Media Foundation decoding of files written by the test, played into a fake (silent) output: no sound device
/// is needed and nothing is heard.
/// </summary>
public sealed class MediaFoundationPlaybackTests : IDisposable
{
    private const int Rate = 44100;
    private const double Frequency = 440;
    private const float Amplitude = 0.5f;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dmxpro-audio-tests", Guid.NewGuid().ToString("N"));

    public MediaFoundationPlaybackTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still mapped by Media Foundation; the temp folder is cleaned by the OS.
        }
    }

    private static float Sine(long frame) => Amplitude * (float)Math.Sin(2 * Math.PI * Frequency * frame / Rate);

    private string WriteSineWav(double seconds, string name = "sine.wav")
    {
        string path = Path.Combine(_folder, name);
        int frames = (int)(seconds * Rate);
        var samples = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            samples[2 * f] = Sine(f);
            samples[(2 * f) + 1] = Sine(f);
        }

        using var writer = new WaveFileWriter(path, new WaveFormat(Rate, 16, 2));
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    private static AudioPlayer CreatePlayer(FakeAudioOutputFactory outputs) =>
        new(outputs, SampleDecoderFactory.Open, null, Timeout.InfiniteTimeSpan);

    [WindowsFact]
    public async Task LoadAsync_SineWav_ReportsDurationAndSeeksWhilePaused()
    {
        string path = WriteSineWav(2.0);
        var outputs = new FakeAudioOutputFactory();
        using AudioPlayer player = CreatePlayer(outputs);

        await player.LoadAsync(path);
        player.Seek(TimeSpan.FromSeconds(1.25));

        Assert.Equal(path, player.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(2), player.Duration);
        Assert.Equal(TimeSpan.FromSeconds(1.25), player.Position);
        Assert.False(player.IsPlaying);
    }

    [WindowsFact]
    public async Task Play_AfterSeek_StartsOnTheExactFrame()
    {
        string path = WriteSineWav(2.0);
        var outputs = new FakeAudioOutputFactory();
        using AudioPlayer player = CreatePlayer(outputs);
        await player.LoadAsync(path);
        const long target = 50_001;

        player.Seek(AudioPlayer.FramesToTime(target, Rate));
        player.Play();
        float[] samples = await Task.Run(() => outputs.Last.Pull(4000)); // the device thread is MTA too
        outputs.Last.PlayedFrames = 1000;

        int transition = (int)Math.Round(Rate * PlaybackSampleProcessor.TransitionSeconds);
        for (int k = transition; k < 4000; k++)
        {
            Assert.True(Math.Abs(Sine(target + k) - samples[2 * k]) < 1e-4, $"frame {k}"); // 16-bit file
        }

        Assert.Equal(AudioPlayer.FramesToTime(target + 1000, Rate), player.Position);
    }

    [WindowsFact]
    public async Task Play_ToTheEnd_StopsAtDuration()
    {
        string path = WriteSineWav(0.5);
        var outputs = new FakeAudioOutputFactory();
        using AudioPlayer player = CreatePlayer(outputs);
        await player.LoadAsync(path);

        player.Play();
        await Task.Run(() => outputs.Last.Pull(Rate));
        outputs.Last.HearAllBut(0);

        Assert.False(player.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(0.5), player.Position);
    }

    [WindowsFact]
    public async Task LoadAsync_DamagedFile_ThrowsAudioException()
    {
        string path = Path.Combine(_folder, "broken.wav");
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray());
        using AudioPlayer player = CreatePlayer(new FakeAudioOutputFactory());

        AudioException error = await Assert.ThrowsAsync<AudioException>(() => player.LoadAsync(path));

        Assert.StartsWith("This file cannot be played", error.Message);
        Assert.Null(player.FilePath);
    }

    [WindowsFact]
    public async Task LoadAsync_MissingFile_ThrowsAudioException()
    {
        using AudioPlayer player = CreatePlayer(new FakeAudioOutputFactory());

        AudioException error = await Assert.ThrowsAsync<AudioException>(() => player.LoadAsync(Path.Combine(_folder, "nope.mp3")));

        Assert.Contains("was not found", error.Message);
    }

    [WindowsFact]
    public async Task StreamingDecoder_WavSeek_LandsOnTheRequestedFrame()
    {
        string wav = WriteSineWav(3.0);
        const long target = 70_001;

        float[] seeked = await Task.Run(() => DecodeRange(() => MediaFoundationDecoder.Open(wav), seek: true, target, 3000));

        for (int k = 0; k < 3000; k++)
        {
            Assert.True(Math.Abs(Sine(target + k) - seeked[2 * k]) < 1e-4, $"frame {k}");
        }
    }

    [WindowsFact]
    public async Task Decoder_Mp3Seek_GivesExactlyTheFramesOfAStraightDecode()
    {
        string wav = WriteSineWav(3.0);
        string mp3 = Path.Combine(_folder, "sine.mp3");
        MediaFoundationApi.Startup();
        try
        {
            using var reader = new WaveFileReader(wav);
            MediaFoundationEncoder.EncodeToMp3(reader, mp3, 192_000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return; // no MP3 encoder on this Windows edition: nothing to decode
        }

        // Media Foundation's own MP3 seek is tens of ms off; the player's decoder must not be.
        const long target = 70_001;
        const int frames = 3000;
        ISampleDecoder Open() => SampleDecoderFactory.Open(mp3, CancellationToken.None);
        float[] continuous = await Task.Run(() => DecodeRange(Open, seek: false, target, frames));
        float[] seeked = await Task.Run(() => DecodeRange(Open, seek: true, target, frames));

        Assert.Equal(continuous, seeked);
    }

    private static float[] DecodeRange(Func<ISampleDecoder> open, bool seek, long target, int frames)
    {
        using ISampleDecoder decoder = open();
        int channels = decoder.Channels;
        var chunk = new float[4096 * channels];
        if (seek)
        {
            decoder.Seek(target);
        }
        else
        {
            long skipped = 0;
            while (skipped < target)
            {
                int want = (int)Math.Min(4096, target - skipped);
                int n = decoder.Read(chunk.AsSpan(0, want * channels), out _);
                Assert.True(n > 0);
                skipped += n;
            }
        }

        var result = new float[frames * channels];
        int done = 0;
        bool first = true;
        while (done < frames)
        {
            int n = decoder.Read(result.AsSpan(done * channels), out long firstFrame);
            Assert.True(n > 0);
            if (first)
            {
                Assert.Equal(target, firstFrame);
                first = false;
            }

            done += n;
        }

        return result;
    }
}
