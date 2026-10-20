namespace DmxPro.Audio.Analysis;

/// <summary>
/// Streaming form of <see cref="WaveformPeaks.Compute"/>: sample <c>n</c> belongs to bucket
/// <c>floor(n * peaksPerSecond / sampleRate)</c>, so fractional bucket sizes (22050 / 100) spread evenly.
/// </summary>
internal sealed class WaveformPeakAccumulator
{
    private readonly long _sampleRate;
    private readonly long _peaksPerSecond;
    private float[] _peaks;
    private int _count;
    private long _sampleIndex;
    private long _bucketStart;
    private long _nextBoundary;
    private float _max;

    public WaveformPeakAccumulator(int sampleRate, int peaksPerSecond, int capacityHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peaksPerSecond);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(peaksPerSecond, sampleRate);

        _sampleRate = sampleRate;
        _peaksPerSecond = peaksPerSecond;
        _peaks = new float[Math.Max(16, capacityHint)];
        _nextBoundary = BucketStart(1);
    }

    /// <summary>Samples consumed so far.</summary>
    public long SampleCount => _sampleIndex;

    public void Add(ReadOnlySpan<float> samples)
    {
        var i = 0;
        while (i < samples.Length)
        {
            var take = (int)Math.Min(_nextBoundary - _sampleIndex, samples.Length - i);
            var max = _max;
            var end = i + take;
            for (; i < end; i++)
            {
                // NaN never compares greater, so corrupt samples cannot poison the bucket.
                var magnitude = Math.Abs(samples[i]);
                if (magnitude > max)
                {
                    max = magnitude;
                }
            }

            _max = max;
            _sampleIndex += take;
            if (_sampleIndex == _nextBoundary)
            {
                Append(_max);
                _max = 0f;
                _bucketStart = _nextBoundary;
                _nextBoundary = BucketStart(_count + 1);
            }
        }
    }

    /// <summary>All complete buckets plus the partial last one (if it has samples).</summary>
    public float[] ToArray()
    {
        var partial = _sampleIndex > _bucketStart ? 1 : 0;
        if (_count + partial == 0)
        {
            return [];
        }

        var result = new float[_count + partial];
        Array.Copy(_peaks, result, _count);
        if (partial == 1)
        {
            result[_count] = Math.Min(_max, 1f);
        }

        return result;
    }

    // First sample of bucket b: ceil(b * sampleRate / peaksPerSecond).
    private long BucketStart(long bucket) => ((bucket * _sampleRate) + _peaksPerSecond - 1) / _peaksPerSecond;

    private void Append(float value)
    {
        if (_count == _peaks.Length)
        {
            Array.Resize(ref _peaks, _peaks.Length * 2);
        }

        _peaks[_count++] = Math.Min(value, 1f);
    }
}
