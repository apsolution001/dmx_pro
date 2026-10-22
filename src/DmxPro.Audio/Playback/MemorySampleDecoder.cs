namespace DmxPro.Audio.Playback;

/// <summary>
/// A song decoded completely into memory (16-bit, interleaved, in 2 MB chunks so no huge array is ever copied).
/// Seeking is plain indexing, so it is exact to the frame and instant, and the audio thread never touches the decoder
/// COM objects. The timeline is exactly that of a straight decode from the start, which is also what the analysis
/// (beats, waveform) sees: lights and music cannot drift apart after a seek.
/// </summary>
internal sealed class MemorySampleDecoder : ISampleDecoder
{
    private const int ChunkShift = 20;
    private const int ChunkSamples = 1 << ChunkShift;
    private const int ChunkMask = ChunkSamples - 1;

    private short[][] _chunks;
    private long _next;

    private MemorySampleDecoder(int sampleRate, int channels, short[][] chunks, long frames)
    {
        SampleRate = sampleRate;
        Channels = channels;
        _chunks = chunks;
        LengthFrames = frames;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public long LengthFrames { get; }

    public long NextFrame => _next;

    /// <summary>
    /// Memory held by the decoded samples, in bytes: the allocated chunks (the last one is allocated whole, so this can
    /// be up to one chunk more than the samples need). 0 after <see cref="Dispose"/>.
    /// </summary>
    public long MemoryBytes => (long)_chunks.Length * ChunkSamples * sizeof(short);

    /// <summary>
    /// Decodes all of <paramref name="source"/> from its current position. Returns null when the song has more than
    /// <paramref name="maxSamples"/> samples (the caller then streams it instead).
    /// </summary>
    public static MemorySampleDecoder? DecodeAll(ISampleDecoder source, long maxSamples, CancellationToken cancellationToken)
    {
        int channels = source.Channels;
        var chunks = new List<short[]>();
        var buffer = new float[8192 * channels];
        long totalSamples = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = source.Read(buffer, out _);
            if (frames == 0)
            {
                break;
            }

            int samples = frames * channels;
            if (totalSamples + samples > maxSamples)
            {
                return null;
            }

            for (int i = 0; i < samples; i++)
            {
                long index = totalSamples + i;
                int chunk = (int)(index >> ChunkShift);
                if (chunk == chunks.Count)
                {
                    chunks.Add(new short[ChunkSamples]);
                }

                chunks[chunk][(int)(index & ChunkMask)] = ToPcm16(buffer[i]);
            }

            totalSamples += samples;
        }

        return new MemorySampleDecoder(source.SampleRate, channels, [.. chunks], totalSamples / channels);
    }

    public int Read(Span<float> destination, out long firstFrame)
    {
        firstFrame = _next;
        int frames = (int)Math.Min(destination.Length / Channels, LengthFrames - _next);
        if (frames <= 0)
        {
            return 0;
        }

        long sample = _next * Channels;
        int samples = frames * Channels;
        int written = 0;
        while (written < samples)
        {
            short[] chunk = _chunks[(int)(sample >> ChunkShift)];
            int offset = (int)(sample & ChunkMask);
            int n = Math.Min(samples - written, ChunkSamples - offset);
            ReadOnlySpan<short> from = chunk.AsSpan(offset, n);
            Span<float> to = destination.Slice(written, n);
            for (int i = 0; i < n; i++)
            {
                to[i] = from[i] * (1f / 32768f);
            }

            written += n;
            sample += n;
        }

        _next += frames;
        return frames;
    }

    public void Seek(long frame) => _next = Math.Clamp(frame, 0, LengthFrames);

    public void Dispose() => _chunks = [];

    private static short ToPcm16(float value)
    {
        float scaled = MathF.Round(value * 32768f);
        return scaled >= short.MaxValue ? short.MaxValue : scaled <= short.MinValue ? short.MinValue : (short)scaled;
    }
}
