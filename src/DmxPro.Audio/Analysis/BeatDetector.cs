namespace DmxPro.Audio.Analysis;

/// <summary>
/// Tempo and beat-grid detection (pure maths, no I/O): onset strength (<see cref="OnsetEnvelope"/>), tempo by
/// autocorrelation with a perceptual weighting and octave check, refinement by folding the whole envelope onto
/// candidate periods, then the grid phase. Deterministic.
/// </summary>
public static class BeatDetector
{
    /// <summary>Lowest tempo searched.</summary>
    public const double MinBpm = 60;

    /// <summary>Highest tempo searched.</summary>
    public const double MaxBpm = 200;

    /// <summary>Shorter signals return no tempo.</summary>
    public const double MinDurationSeconds = 4.0;

    /// <summary>Signals whose peak stays below this (-60 dBFS) count as silence.</summary>
    public const float SilenceAmplitude = 0.001f;

    // Local-mean window used to remove slow loudness changes from the envelope.
    private const double DetrendWindowSeconds = 0.3;

    // Perceptual preference: full weight in this range, log-Gaussian fall-off (sigma in octaves) outside.
    private const double PreferredLowBpm = 90;
    private const double PreferredHighBpm = 140;
    private const double WeightSigmaOctaves = 0.5;

    // Double tempo wins when its autocorrelation is at least this fraction of the chosen one (evenly strong pulses).
    private const double DoubleTempoRatio = 0.8;

    // Steady-beat test. The autocorrelation of an uncorrelated envelope has a spread of ~1.7/sqrt(frames) (the
    // 4x-overlapping frames correlate neighbours), so the peak must stand out from the lag-range mean by far more than
    // that, and also be a meaningful correlation in absolute terms.
    private const double MinContrastNoiseUnits = 6.0;
    private const double MinPeakCorrelation = 0.08;

    // Fold refinement: search +-1 % around the autocorrelation estimate in 0.01 BPM steps.
    private const double RefineRangeFraction = 0.01;
    private const double RefineStepBpm = 0.01;

    // A grid beat is present once the envelope near it is this far from the off-beat level towards a typical beat;
    // the first beat and the next one must be present, and at least 3/4 of a run of FirstBeatRun beats.
    private const double FirstBeatThreshold = 0.35;
    private const double FirstBeatToleranceSeconds = 0.012;
    private const int FirstBeatRun = 8;

    private static readonly (double? Bpm, double FirstBeatSeconds) NoBeat = (null, 0);

    /// <summary>
    /// Detects the tempo and the time of the first beat of a mono signal. Returns (null, 0) for silence, very short
    /// signals or no steady beat. BPM is rounded to 0.1.
    /// </summary>
    public static (double? Bpm, double FirstBeatSeconds) Detect(ReadOnlySpan<float> mono, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        var envelope = new OnsetEnvelope(sampleRate, (double)mono.Length / sampleRate);
        envelope.Add(mono);
        return Detect(envelope);
    }

    /// <summary>Same as <see cref="Detect(ReadOnlySpan{float}, int)"/> on an envelope built while streaming.</summary>
    public static (double? Bpm, double FirstBeatSeconds) Detect(OnsetEnvelope envelope) => Detect(envelope, CancellationToken.None);

    internal static (double? Bpm, double FirstBeatSeconds) Detect(OnsetEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.PeakAmplitude < SilenceAmplitude)
        {
            return NoBeat;
        }

        return DetectFromEnvelope(envelope.Values, envelope.FrameRate, envelope.FrameOffsetSeconds, cancellationToken);
    }

    /// <summary>
    /// Tempo and first beat from a precomputed onset-strength envelope sampled at <paramref name="frameRate"/>, where
    /// frame <c>i</c> is at <c>frameOffsetSeconds + i / frameRate</c>.
    /// </summary>
    public static (double? Bpm, double FirstBeatSeconds) DetectFromEnvelope(ReadOnlySpan<float> onsetStrength, double frameRate, double frameOffsetSeconds = 0) =>
        DetectFromEnvelope(onsetStrength, frameRate, frameOffsetSeconds, CancellationToken.None);

    internal static (double? Bpm, double FirstBeatSeconds) DetectFromEnvelope(
        ReadOnlySpan<float> onsetStrength, double frameRate, double frameOffsetSeconds, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(frameRate) || frameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate), frameRate, "Frame rate must be positive.");
        }

        var frames = onsetStrength.Length;
        var minLag = (int)Math.Ceiling(60.0 * frameRate / MaxBpm);
        var maxLag = (int)Math.Floor(60.0 * frameRate / MinBpm);
        if (frames < MinDurationSeconds * frameRate || minLag < 2 || maxLag + 2 >= frames)
        {
            return NoBeat;
        }

        var detrended = Detrend(onsetStrength, frameRate);
        if (detrended is null)
        {
            return NoBeat;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var correlation = Autocorrelate(Smooth(detrended), minLag - 1, maxLag + 1);
        if (correlation is null)
        {
            return NoBeat;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var lag = ChooseTempoLag(correlation, minLag, maxLag, frameRate, frames);
        if (lag < 0)
        {
            return NoBeat;
        }

        var coarseBpm = 60.0 * frameRate / InterpolatePeak(correlation, lag).Position;
        var onsets = OnsetList.From(detrended);
        var refinedBpm = RefineTempo(onsets, frameRate, frameOffsetSeconds, coarseBpm, cancellationToken);
        var bpm = Math.Clamp(Math.Round(refinedBpm, 1, MidpointRounding.AwayFromZero), MinBpm, MaxBpm);

        // Phase is fitted with the rounded tempo, because that is the grid the app will draw.
        var period = 60.0 / bpm;
        var bins = FoldBinCount(period, frameRate);
        FoldScore(onsets, frameRate, frameOffsetSeconds, period, new double[bins], new double[bins], out var phase);
        var first = FirstBeat(detrended, frameRate, frameOffsetSeconds, period, phase);
        return (bpm, first);
    }

    /// <summary>Perceptual tempo weight: 1 inside 90–140 BPM, log-Gaussian fall-off (in octaves) outside.</summary>
    internal static double TempoWeight(double bpm)
    {
        double octaves;
        if (bpm < PreferredLowBpm)
        {
            octaves = Math.Log2(PreferredLowBpm / bpm);
        }
        else if (bpm > PreferredHighBpm)
        {
            octaves = Math.Log2(bpm / PreferredHighBpm);
        }
        else
        {
            return 1.0;
        }

        var z = octaves / WeightSigmaOctaves;
        return Math.Exp(-0.5 * z * z);
    }

    // Subtract a centred moving average and half-wave rectify: keeps the onset peaks, removes loudness drift.
    // Returns null when nothing is left (flat envelope).
    private static float[]? Detrend(ReadOnlySpan<float> envelope, double frameRate)
    {
        var frames = envelope.Length;
        var prefix = new double[frames + 1];
        for (var i = 0; i < frames; i++)
        {
            var value = envelope[i];
            prefix[i + 1] = prefix[i] + (float.IsFinite(value) && value > 0 ? value : 0);
        }

        var half = Math.Max(1, (int)Math.Round(DetrendWindowSeconds * frameRate / 2));
        var result = new float[frames];
        var max = 0f;
        for (var i = 0; i < frames; i++)
        {
            var lo = Math.Max(0, i - half);
            var hi = Math.Min(frames, i + half + 1);
            var mean = (prefix[hi] - prefix[lo]) / (hi - lo);
            var value = (prefix[i + 1] - prefix[i]) - mean;
            var rectified = value > 0 ? (float)value : 0f;
            result[i] = rectified;
            if (rectified > max)
            {
                max = rectified;
            }
        }

        return max > 1e-9f ? result : null;
    }

    // [1 2 1] / 4 smoothing: widens one-frame onset spikes so autocorrelation peaks do not fall between two lags.
    private static float[] Smooth(float[] signal)
    {
        var result = new float[signal.Length];
        var last = signal.Length - 1;
        for (var i = 0; i <= last; i++)
        {
            var left = signal[i > 0 ? i - 1 : 0];
            var right = signal[i < last ? i + 1 : last];
            result[i] = (left + (2 * signal[i]) + right) * 0.25f;
        }

        return result;
    }

    // Normalised autocorrelation rho[lag] for lag in [fromLag, toLag] (index = lag), mean removed, unbiased per lag.
    private static double[]? Autocorrelate(float[] signal, int fromLag, int toLag)
    {
        var frames = signal.Length;
        double sum = 0;
        for (var i = 0; i < frames; i++)
        {
            sum += signal[i];
        }

        var mean = sum / frames;
        var centred = new double[frames];
        double energy = 0;
        for (var i = 0; i < frames; i++)
        {
            var value = signal[i] - mean;
            centred[i] = value;
            energy += value * value;
        }

        var variance = energy / frames;
        if (variance <= 1e-18)
        {
            return null;
        }

        var result = new double[toLag + 1];
        for (var lag = fromLag; lag <= toLag; lag++)
        {
            double acc = 0;
            var count = frames - lag;
            for (var i = 0; i < count; i++)
            {
                acc += centred[i] * centred[i + lag];
            }

            result[lag] = acc / count / variance;
        }

        return result;
    }

    // Picks the beat lag: best weighted local maximum, steady-beat test, then the octave check towards double tempo.
    // Returns -1 when there is no clear periodicity.
    private static int ChooseTempoLag(double[] rho, int minLag, int maxLag, double frameRate, int frames)
    {
        var best = -1;
        var bestScore = 0.0;
        double sum = 0;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            sum += rho[lag];
            if (!IsLocalMax(rho, lag) || rho[lag] <= 0)
            {
                continue;
            }

            var (position, height) = InterpolatePeak(rho, lag);
            var score = height * TempoWeight(60.0 * frameRate / position);
            if (score > bestScore)
            {
                bestScore = score;
                best = lag;
            }
        }

        if (best < 0)
        {
            return -1;
        }

        var mean = sum / (maxLag - minLag + 1);
        var noise = 1.7 / Math.Sqrt(frames);
        var bestHeight = InterpolatePeak(rho, best).Height;
        if (bestHeight < MinPeakCorrelation || bestHeight - mean < MinContrastNoiseUnits * noise)
        {
            return -1;
        }

        // A track of evenly strong pulses at 174 BPM correlates as well at 87 BPM; the faster pulse is the beat.
        var halfLag = best / 2.0;
        if (60.0 * frameRate / halfLag <= MaxBpm)
        {
            var from = Math.Max(minLag, (int)Math.Floor(halfLag) - 1);
            var to = Math.Min(maxLag, (int)Math.Ceiling(halfLag) + 1);
            var doubleLag = -1;
            var doubleHeight = 0.0;
            for (var lag = from; lag <= to; lag++)
            {
                if (IsLocalMax(rho, lag))
                {
                    var height = InterpolatePeak(rho, lag).Height;
                    if (doubleLag < 0 || height > doubleHeight)
                    {
                        doubleLag = lag;
                        doubleHeight = height;
                    }
                }
            }

            if (doubleLag >= 0 && doubleHeight >= DoubleTempoRatio * bestHeight)
            {
                best = doubleLag;
            }
        }

        return best;
    }

    private static bool IsLocalMax(double[] values, int index) =>
        values[index] >= values[index - 1] && values[index] > values[index + 1];

    // Sub-frame position and height of the peak at index by parabolic interpolation.
    private static (double Position, double Height) InterpolatePeak(double[] values, int index)
    {
        var left = values[index - 1];
        var centre = values[index];
        var right = values[index + 1];
        var denominator = left - (2 * centre) + right;
        if (Math.Abs(denominator) < 1e-12)
        {
            return (index, centre);
        }

        var delta = Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5);
        return (index + delta, centre - (0.25 * (left - right) * delta));
    }

    // Folds the whole envelope onto each candidate period around the estimate; the period whose beats line up best
    // over the full song wins. Over long songs this is far more precise than the autocorrelation lag.
    private static double RefineTempo(OnsetList onsets, double frameRate, double frameOffsetSeconds, double estimateBpm, CancellationToken cancellationToken)
    {
        var low = Math.Max(MinBpm, estimateBpm * (1 - RefineRangeFraction));
        var high = Math.Min(MaxBpm, estimateBpm * (1 + RefineRangeFraction));
        var steps = (int)Math.Floor((high - low) / RefineStepBpm);
        var bins = FoldBinCount(60.0 / estimateBpm, frameRate);
        var histogram = new double[bins];
        var smoothed = new double[bins];

        var bestBpm = estimateBpm;
        var bestScore = double.NegativeInfinity;
        for (var step = 0; step <= steps; step++)
        {
            if ((step & 15) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var candidate = low + (step * RefineStepBpm);
            var score = FoldScore(onsets, frameRate, frameOffsetSeconds, 60.0 / candidate, histogram, smoothed, out _);
            if (score > bestScore)
            {
                bestScore = score;
                bestBpm = candidate;
            }
        }

        return bestBpm;
    }

    private static int FoldBinCount(double period, double frameRate) => Math.Max(8, (int)Math.Round(period * frameRate));

    // Accumulates the onset strength at each frame's phase within the period (linear split between two bins),
    // smooths circularly and returns the peak height; phase = time of that peak within [0, period).
    private static double FoldScore(OnsetList onsets, double frameRate, double frameOffsetSeconds, double period, double[] histogram, double[] smoothed, out double phase)
    {
        var bins = histogram.Length;
        Array.Clear(histogram);
        var binsPerSecond = bins / period;
        var frameSeconds = 1.0 / frameRate;
        var indices = onsets.Indices;
        var weights = onsets.Weights;
        for (var j = 0; j < indices.Length; j++)
        {
            var u = (frameOffsetSeconds + (indices[j] * frameSeconds)) * binsPerSecond;
            u -= Math.Floor(u / bins) * bins;
            var lower = (int)u;
            if (lower >= bins)
            {
                lower = 0;
                u = 0;
            }

            var fraction = u - lower;
            var upper = lower + 1 == bins ? 0 : lower + 1;
            histogram[lower] += weights[j] * (1 - fraction);
            histogram[upper] += weights[j] * fraction;
        }

        // Circular smoothing with a small binomial kernel [1 4 6 4 1] / 16 keeps the score smooth in the period.
        var bestBin = 0;
        for (var b = 0; b < bins; b++)
        {
            var value = (6 * histogram[b])
                + (4 * (histogram[Wrap(b - 1, bins)] + histogram[Wrap(b + 1, bins)]))
                + histogram[Wrap(b - 2, bins)] + histogram[Wrap(b + 2, bins)];
            smoothed[b] = value / 16;
            if (smoothed[b] > smoothed[bestBin])
            {
                bestBin = b;
            }
        }

        var left = smoothed[Wrap(bestBin - 1, bins)];
        var centre = smoothed[bestBin];
        var right = smoothed[Wrap(bestBin + 1, bins)];
        var denominator = left - (2 * centre) + right;
        var delta = Math.Abs(denominator) < 1e-12 ? 0 : Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5);
        var position = bestBin + delta;
        if (position < 0)
        {
            position += bins;
        }

        phase = position / binsPerSecond;
        if (phase >= period)
        {
            phase -= period;
        }

        return centre;
    }

    private static int Wrap(int index, int length)
    {
        if (index < 0)
        {
            return index + length;
        }

        return index >= length ? index - length : index;
    }

    // First grid beat (phase + k * period) where the music actually has onsets: skips silent or beat-less intros.
    // A beat is "present" when the envelope near it rises clearly above the off-beat level (half a period later), and
    // the first beat must start a run where most of the next beats are present too, so a stray noise peak in the
    // intro does not count.
    private static double FirstBeat(float[] detrended, double frameRate, double frameOffsetSeconds, double period, double phase)
    {
        var frames = detrended.Length;
        var duration = frameOffsetSeconds + (frames / frameRate);
        var start = phase - period >= -FirstBeatToleranceSeconds ? -1 : 0;
        var count = (int)Math.Floor((duration - phase) / period) + 1 - start;
        var fallback = Math.Max(0, phase + (start * period));
        if (count <= 0)
        {
            return fallback;
        }

        var onBeat = new float[count];
        var offBeat = new float[count];
        for (var k = 0; k < count; k++)
        {
            var time = phase + ((k + start) * period);
            onBeat[k] = WindowMax(detrended, frameRate, frameOffsetSeconds, time);
            offBeat[k] = WindowMax(detrended, frameRate, frameOffsetSeconds, time + (period / 2));
        }

        var typical = Percentile(onBeat, 0.75);
        var baseline = Percentile(offBeat, 0.5);
        if (typical <= baseline)
        {
            return fallback;
        }

        var threshold = baseline + (FirstBeatThreshold * (typical - baseline));
        for (var k = 0; k < count; k++)
        {
            if (onBeat[k] < threshold || (k + 1 < count && onBeat[k + 1] < threshold))
            {
                continue;
            }

            var run = Math.Min(FirstBeatRun, count - k);
            var present = 0;
            for (var j = 0; j < run; j++)
            {
                if (onBeat[k + j] >= threshold)
                {
                    present++;
                }
            }

            if (present * 4 >= run * 3)
            {
                return Math.Max(0, phase + ((k + start) * period));
            }
        }

        return fallback;
    }

    private static float WindowMax(float[] values, double frameRate, double frameOffsetSeconds, double time)
    {
        var from = Math.Max(0, (int)Math.Ceiling((time - FirstBeatToleranceSeconds - frameOffsetSeconds) * frameRate));
        var to = Math.Min(values.Length - 1, (int)Math.Floor((time + FirstBeatToleranceSeconds - frameOffsetSeconds) * frameRate));
        var max = 0f;
        for (var i = from; i <= to; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        return max;
    }

    private static float Percentile(float[] values, double fraction)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[(int)(fraction * (sorted.Length - 1))];
    }

    // Frames with non-zero detrended onset strength (about half of them): the fold only needs those.
    private sealed class OnsetList
    {
        private OnsetList(int[] indices, float[] weights)
        {
            Indices = indices;
            Weights = weights;
        }

        public int[] Indices { get; }

        public float[] Weights { get; }

        public static OnsetList From(float[] detrended)
        {
            var count = 0;
            for (var i = 0; i < detrended.Length; i++)
            {
                if (detrended[i] > 0)
                {
                    count++;
                }
            }

            var indices = new int[count];
            var weights = new float[count];
            var j = 0;
            for (var i = 0; i < detrended.Length; i++)
            {
                if (detrended[i] > 0)
                {
                    indices[j] = i;
                    weights[j] = detrended[i];
                    j++;
                }
            }

            return new OnsetList(indices, weights);
        }
    }
}
