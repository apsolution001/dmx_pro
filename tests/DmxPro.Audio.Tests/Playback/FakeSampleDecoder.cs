using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

/// <summary>Synthetic decoder: every sample of frame f is <see cref="ValueAt"/>(f); returns at most <see cref="ChunkFrames"/> per read.</summary>
internal sealed class FakeSampleDecoder(int sampleRate, int channels, long lengthFrames) : ISampleDecoder
{
    private long _next;

    public int SampleRate { get; } = sampleRate;

    public int Channels { get; } = channels;

    public long LengthFrames { get; } = lengthFrames;

    public long NextFrame => _next;

    public int ChunkFrames { get; set; } = 441;

    public Func<long, float> ValueAt { get; set; } = _ => 0.5f;

    /// <summary>Read throws this once the decoder reaches <see cref="FailAtFrame"/>.</summary>
    public long FailAtFrame { get; set; } = -1;

    public int SeekCount { get; private set; }

    public bool IsDisposed { get; private set; }

    public int Read(Span<float> destination, out long firstFrame)
    {
        firstFrame = _next;
        if (FailAtFrame >= 0 && _next >= FailAtFrame && destination.Length > 0)
        {
            throw new InvalidOperationException("synthetic decode failure");
        }

        long available = Math.Max(0, (FailAtFrame >= 0 ? Math.Min(FailAtFrame, LengthFrames) : LengthFrames) - _next);
        int frames = (int)Math.Min(Math.Min(destination.Length / Channels, ChunkFrames), available);
        for (int f = 0; f < frames; f++)
        {
            float value = ValueAt(_next + f);
            for (int c = 0; c < Channels; c++)
            {
                destination[(f * Channels) + c] = value;
            }
        }

        _next += frames;
        return frames;
    }

    public void Seek(long frame)
    {
        SeekCount++;
        _next = Math.Clamp(frame, 0, LengthFrames);
    }

    public void Dispose() => IsDisposed = true;
}
