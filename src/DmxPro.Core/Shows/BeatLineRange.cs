namespace DmxPro.Core.Shows;

/// <summary>
/// Allocation-free sequence of beat lines in a time range (from <see cref="TimelineSnap.BeatLines"/>); use with
/// <c>foreach</c>. Times are computed from the beat index each step, so long shows do not drift.
/// </summary>
public readonly struct BeatLineRange
{
    private readonly long _firstIndex;
    private readonly int _stride;
    private readonly double _toSeconds;
    private readonly double _origin;
    private readonly double _beatSeconds;

    internal BeatLineRange(long firstIndex, int stride, double toSeconds, double origin, double beatSeconds)
    {
        _firstIndex = firstIndex;
        _stride = stride;
        _toSeconds = toSeconds;
        _origin = origin;
        _beatSeconds = beatSeconds;
    }

    public Enumerator GetEnumerator() => new(_firstIndex, _stride, _toSeconds, _origin, _beatSeconds);

    /// <summary>Enumerator over <see cref="BeatLineRange"/>.</summary>
    public struct Enumerator
    {
        private readonly int _stride;
        private readonly double _toSeconds;
        private readonly double _origin;
        private readonly double _beatSeconds;
        private long _index;
        private int _produced;
        private BeatLine _current;

        internal Enumerator(long firstIndex, int stride, double toSeconds, double origin, double beatSeconds)
        {
            _stride = stride;
            _toSeconds = toSeconds;
            _origin = origin;
            _beatSeconds = beatSeconds;
            _index = firstIndex;
            _produced = 0;
            _current = default;
        }

        public readonly BeatLine Current => _current;

        public bool MoveNext()
        {
            // A default range (stride 0) is empty.
            if (_stride <= 0 || _produced >= TimelineSnap.MaxBeatLines)
            {
                return false;
            }

            var seconds = _origin + (_index * _beatSeconds);
            if (!(seconds <= _toSeconds + 1e-9))
            {
                return false;
            }

            // A line at show time 0 may come out as -1e-17 from rounding; never report a time before 0.
            _current = new BeatLine(Math.Max(0, seconds), _index, TimelineSnap.IsBarStart(_index));
            _index += _stride;
            _produced++;
            return true;
        }
    }
}
