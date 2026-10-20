namespace DmxPro.Audio.Playback;

/// <summary>What a run of output frames contains.</summary>
internal enum OutputSpanKind : byte
{
    /// <summary>Song audio; the song position advances one frame per output frame.</summary>
    Audio,

    /// <summary>Silence while halted (paused / stopped); the song position holds.</summary>
    Halted,

    /// <summary>Silence after the end of the song (or a decode failure); the song position holds at the end.</summary>
    End,
}

/// <summary>Song position of one output frame, as found by <see cref="HeardPositionMap.TryMap"/>.</summary>
internal readonly record struct HeardFrame(long SourceFrame, int Generation, OutputSpanKind Kind);

/// <summary>
/// Remembers, for the most recent output frames handed to the device, which song frame each one carries. The device
/// reports how many frames it has actually played (= is being heard); looking that number up here gives the song
/// position being heard, with every buffer, resampler and latency delay automatically accounted for, and seeks / pauses
/// handled exactly at the frame where they happened. A fixed ring of spans: no allocations. Not thread-safe; the
/// owner locks.
/// </summary>
internal sealed class HeardPositionMap
{
    private readonly long[] _outputStart;
    private readonly long[] _sourceStart;
    private readonly int[] _count;
    private readonly int[] _generation;
    private readonly OutputSpanKind[] _kind;
    private int _head; // index of the newest span
    private int _size;

    public HeardPositionMap(int capacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _outputStart = new long[capacity];
        _sourceStart = new long[capacity];
        _count = new int[capacity];
        _generation = new int[capacity];
        _kind = new OutputSpanKind[capacity];
    }

    /// <summary>Total output frames recorded since the last <see cref="Reset"/>.</summary>
    public long OutputFrames { get; private set; }

    /// <summary>
    /// First output frame of the current trailing run of silence (Halted/End), or -1 when the newest span is audio
    /// or nothing was recorded.
    /// </summary>
    public long SilentSinceOutputFrame { get; private set; } = -1;

    /// <summary>Forgets everything; the next recorded frame is output frame 0 (the device restarted).</summary>
    public void Reset()
    {
        _head = 0;
        _size = 0;
        OutputFrames = 0;
        SilentSinceOutputFrame = -1;
    }

    /// <summary>Appends <paramref name="count"/> output frames; merges with the previous span when contiguous.</summary>
    public void Record(int generation, OutputSpanKind kind, long sourceStart, int count)
    {
        if (count <= 0)
        {
            return;
        }

        long outputStart = OutputFrames;
        OutputFrames += count;

        if (kind == OutputSpanKind.Audio)
        {
            SilentSinceOutputFrame = -1;
        }
        else if (SilentSinceOutputFrame < 0)
        {
            SilentSinceOutputFrame = outputStart;
        }

        if (_size > 0)
        {
            int h = _head;
            bool sameRun = _generation[h] == generation && _kind[h] == kind && _count[h] <= int.MaxValue - count;
            bool contiguous = kind == OutputSpanKind.Audio
                ? _sourceStart[h] + _count[h] == sourceStart
                : _sourceStart[h] == sourceStart;
            if (sameRun && contiguous)
            {
                _count[h] += count;
                return;
            }
        }

        _head = _size == 0 ? 0 : (_head + 1) % _outputStart.Length;
        if (_size < _outputStart.Length)
        {
            _size++;
        }

        _outputStart[_head] = outputStart;
        _sourceStart[_head] = sourceStart;
        _count[_head] = count;
        _generation[_head] = generation;
        _kind[_head] = kind;
    }

    /// <summary>
    /// Song frame carried by output frame <paramref name="outputFrame"/>. Frames past the newest span extrapolate it
    /// (the device clock can run a hair ahead of what was written); frames older than the ring return false.
    /// </summary>
    public bool TryMap(long outputFrame, out HeardFrame result)
    {
        result = default;
        if (_size == 0 || outputFrame < 0)
        {
            return false;
        }

        int index = _head;
        for (int i = 0; i < _size; i++)
        {
            if (outputFrame >= _outputStart[index])
            {
                // Spans are contiguous, so only the newest span can be overrun: hold at its end.
                long offset = Math.Min(outputFrame - _outputStart[index], _count[index]);

                long source = _kind[index] == OutputSpanKind.Audio ? _sourceStart[index] + offset : _sourceStart[index];
                result = new HeardFrame(source, _generation[index], _kind[index]);
                return true;
            }

            index = index == 0 ? _outputStart.Length - 1 : index - 1;
        }

        return false;
    }
}
