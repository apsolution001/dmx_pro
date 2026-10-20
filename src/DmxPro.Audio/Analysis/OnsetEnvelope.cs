namespace DmxPro.Audio.Analysis;

/// <summary>
/// Streaming onset-strength envelope (spectral flux): mono samples are pushed in chunks with <see cref="Add"/>; every
/// <see cref="HopSize"/> samples a Hann-windowed FFT frame of <see cref="WindowSize"/> samples is taken, bin powers are
/// grouped into quarter-octave bands (averaging many bins keeps noise from dominating the flux), log-compressed, and the
/// half-wave-rectified increase over the previous frame is averaged over the bands. Window ≈ 23 ms and
/// hop = window / 4, so the frame rate is ≈ 170–190 Hz for common sample rates. Memory is O(frames), never O(samples).
/// </summary>
public sealed class OnsetEnvelope
{
    private const double TargetWindowSeconds = 0.0232;
    private const double CompressionGain = 100.0;
    private const double BandsPerOctave = 4;
    private const double MaxBandFrequency = 11025;
    private const double OnsetLagHops = 1.45;

    private readonly Fft _fft;
    private readonly double[] _window;
    private readonly float[] _history;
    private readonly double[] _frame;
    private readonly double[] _power;
    private readonly int[] _bandOfBin;
    private readonly int[] _binsInBand;
    private readonly double[] _bandPower;
    private readonly double[] _previousLog;
    private readonly int _bandCount;
    private readonly int _lastBin;
    private readonly double _magnitudeScale;
    private float[] _values;
    private int _count;
    private int _historyPosition;
    private int _pending;
    private long _samplesAdded;
    private float _peakAmplitude;

    /// <param name="sampleRate">Sample rate of the mono samples that will be added.</param>
    /// <param name="expectedSeconds">Optional length hint so the envelope buffer is allocated once.</param>
    public OnsetEnvelope(int sampleRate, double expectedSeconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        SampleRate = sampleRate;
        var exponent = (int)Math.Round(Math.Log2(Math.Max(1.0, sampleRate * TargetWindowSeconds)));
        WindowSize = 1 << Math.Clamp(exponent, 5, 15);
        HopSize = WindowSize / 4;

        _fft = new Fft(WindowSize);
        _window = new double[WindowSize];
        double windowSum = 0;
        for (var i = 0; i < WindowSize; i++)
        {
            _window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / WindowSize));
            windowSum += _window[i];
        }

        // A full-scale sine then shows magnitude ~1 in its bin, so the compression gain means the same at every rate.
        _magnitudeScale = 2.0 / windowSum;
        _history = new float[WindowSize];
        _frame = new double[WindowSize];
        _power = new double[(WindowSize / 2) + 1];
        (_bandOfBin, _binsInBand, _lastBin) = BuildBands(sampleRate, WindowSize);
        _bandCount = _binsInBand.Length;
        _bandPower = new double[_bandCount];
        _previousLog = new double[_bandCount];

        var hint = double.IsFinite(expectedSeconds) && expectedSeconds > 0
            ? (int)Math.Min(int.MaxValue / 2, (expectedSeconds * sampleRate / HopSize) + 16)
            : 1024;
        _values = new float[Math.Max(16, hint)];
    }

    public int SampleRate { get; }

    /// <summary>FFT window length in samples.</summary>
    public int WindowSize { get; }

    /// <summary>Samples between two envelope frames.</summary>
    public int HopSize { get; }

    /// <summary>Envelope frames per second.</summary>
    public double FrameRate => (double)SampleRate / HopSize;

    /// <summary>
    /// Time of frame 0 in seconds: frame <c>i</c> describes an onset at about <c>FrameOffsetSeconds + i / FrameRate</c>.
    /// Frame <c>i</c>'s window ends at sample <c>(i + 1) * HopSize</c>; a percussive onset gives its largest flux once
    /// it is ~1.4 hops inside the window (the Hann taper still damps it before that), so frame <c>i</c> is placed
    /// <see cref="OnsetLagHops"/> hops before its window end. Calibrated on click tracks to within ~1 ms.
    /// </summary>
    public double FrameOffsetSeconds => (1.0 - OnsetLagHops) * HopSize / SampleRate;

    /// <summary>Number of envelope frames so far.</summary>
    public int Count => _count;

    /// <summary>Largest absolute sample seen (used to recognise silence).</summary>
    public float PeakAmplitude => _peakAmplitude;

    /// <summary>Length of the audio added so far.</summary>
    public double DurationSeconds => (double)_samplesAdded / SampleRate;

    /// <summary>The onset-strength values (non-negative), one per frame.</summary>
    public ReadOnlySpan<float> Values => _values.AsSpan(0, _count);

    /// <summary>Pushes the next mono samples.</summary>
    public void Add(ReadOnlySpan<float> samples)
    {
        var peak = _peakAmplitude;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (!float.IsFinite(sample))
            {
                sample = 0f;
            }

            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }

            _history[_historyPosition] = sample;
            _historyPosition++;
            if (_historyPosition == WindowSize)
            {
                _historyPosition = 0;
            }

            _pending++;
            if (_pending == HopSize)
            {
                _pending = 0;
                ComputeFrame();
            }
        }

        _peakAmplitude = peak;
        _samplesAdded += samples.Length;
    }

    private void ComputeFrame()
    {
        // The oldest sample sits at the next write position of the ring buffer.
        var j = 0;
        for (var i = _historyPosition; i < WindowSize; i++, j++)
        {
            _frame[j] = _history[i] * _window[j];
        }

        for (var i = 0; i < _historyPosition; i++, j++)
        {
            _frame[j] = _history[i] * _window[j];
        }

        _fft.PowerSpectrum(_frame, _power);

        Array.Clear(_bandPower);
        for (var k = 1; k <= _lastBin; k++)
        {
            _bandPower[_bandOfBin[k]] += _power[k];
        }

        double flux = 0;
        for (var b = 0; b < _bandCount; b++)
        {
            var magnitude = Math.Sqrt(_bandPower[b] / _binsInBand[b]) * _magnitudeScale;
            var log = Math.Log(1.0 + (CompressionGain * magnitude));
            var rise = log - _previousLog[b];
            if (rise > 0)
            {
                flux += rise;
            }

            _previousLog[b] = log;
        }

        if (_count == _values.Length)
        {
            Array.Resize(ref _values, _values.Length * 2);
        }

        _values[_count++] = (float)(flux / _bandCount);
    }

    // Maps FFT bins 1..lastBin to quarter-octave bands (low bins get one band each, high bands merge many bins).
    private static (int[] BandOfBin, int[] BinsInBand, int LastBin) BuildBands(int sampleRate, int windowSize)
    {
        var binHz = (double)sampleRate / windowSize;
        var lastBin = Math.Max(1, Math.Min(windowSize / 2, (int)Math.Floor(MaxBandFrequency / binHz)));
        var bandOfBin = new int[lastBin + 1];
        var counts = new List<int>();
        var previousKey = int.MinValue;
        for (var k = 1; k <= lastBin; k++)
        {
            var key = (int)Math.Floor(Math.Log2(k) * BandsPerOctave);
            if (key != previousKey)
            {
                counts.Add(0);
                previousKey = key;
            }

            bandOfBin[k] = counts.Count - 1;
            counts[^1]++;
        }

        return (bandOfBin, counts.ToArray(), lastBin);
    }
}
