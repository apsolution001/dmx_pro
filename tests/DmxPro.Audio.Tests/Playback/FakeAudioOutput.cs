using DmxPro.Audio.Playback;
using NAudio.Wave;

namespace DmxPro.Audio.Tests.Playback;

/// <summary>Silent stand-in for the sound device: the test pulls samples and sets the played position by hand.</summary>
internal sealed class FakeAudioOutput : IAudioOutput
{
    private ISampleProvider? _source;

    public event EventHandler<Exception?>? Failed;

    public bool IsRunning { get; private set; }

    public int PlayCount { get; private set; }

    public int StopCount { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Total frames pulled from the source since the last Play.</summary>
    public long PulledFrames { get; private set; }

    public long PlayedFrames { get; set; }

    public int Channels => _source!.WaveFormat.Channels;

    public void Init(ISampleProvider source) => _source = source;

    public void Play()
    {
        PlayCount++;
        IsRunning = true;
        PlayedFrames = 0;
        PulledFrames = 0;
    }

    public void Stop()
    {
        if (IsRunning)
        {
            StopCount++;
        }

        IsRunning = false;
        PlayedFrames = 0;
    }

    /// <summary>Reads <paramref name="frames"/> frames like the device would (interleaved samples returned).</summary>
    public float[] Pull(int frames)
    {
        var buffer = new float[frames * Channels];
        int read = _source!.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, read);
        PulledFrames += frames;
        return buffer;
    }

    /// <summary>Simulates the device having played everything pulled so far, minus <paramref name="bufferedFrames"/>.</summary>
    public void HearAllBut(long bufferedFrames) => PlayedFrames = Math.Max(0, PulledFrames - bufferedFrames);

    public void RaiseFailed(Exception? exception) => Failed?.Invoke(this, exception);

    public void Dispose()
    {
        Stop();
        IsDisposed = true;
    }
}
