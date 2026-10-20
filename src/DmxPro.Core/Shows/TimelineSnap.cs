namespace DmxPro.Core.Shows;

/// <summary>
/// Snapping and beat-grid math for the show timeline editor (beats and bars come from the show's BPM, 4/4). The grid
/// can be shifted by a beat offset (<see cref="Show.BeatOffsetSeconds"/>, the time of the first beat of the music); it
/// continues backwards from the offset down to show time 0.
/// </summary>
public static class TimelineSnap
{
    /// <summary>Beats per bar; shows are 4/4 for now.</summary>
    public const int BeatsPerBar = 4;

    /// <summary>Precision used when snapping is off: 1 ms.</summary>
    public const double OffStepSeconds = 0.001;

    /// <summary>Upper bound on the lines one <see cref="BeatLines"/> call yields, so a silly BPM or range cannot hang the UI.</summary>
    public const int MaxBeatLines = 100_000;

    /// <summary>Seconds per beat for <paramref name="bpm"/>; a non-positive or non-finite BPM falls back to <see cref="Show.DefaultBpm"/>.</summary>
    public static double BeatSeconds(double bpm) =>
        60.0 / (double.IsFinite(bpm) && bpm > 0 ? bpm : Show.DefaultBpm);

    /// <summary>Seconds per bar (<see cref="BeatsPerBar"/> beats).</summary>
    public static double BarSeconds(double bpm) => BeatSeconds(bpm) * BeatsPerBar;

    /// <summary>Grid step in seconds for a snap mode.</summary>
    public static double StepSeconds(TimelineSnapMode mode, double bpm) => mode switch
    {
        TimelineSnapMode.Tenth => 0.1,
        TimelineSnapMode.Beat => BeatSeconds(bpm),
        TimelineSnapMode.Bar => BarSeconds(bpm),
        _ => OffStepSeconds,
    };

    /// <summary>
    /// Rounds <paramref name="seconds"/> to the nearest grid step (never below 0). Non-finite input becomes 0.
    /// The beat grid starts at 0 (no offset).
    /// </summary>
    public static double Snap(double seconds, TimelineSnapMode mode, double bpm) => Snap(seconds, mode, bpm, 0);

    /// <summary>
    /// Rounds <paramref name="seconds"/> to the nearest grid line (never below 0; show time 0 is always a snap point).
    /// Beat and bar lines are shifted by <paramref name="beatOffsetSeconds"/> (a non-finite offset counts as 0);
    /// tenths and "off" ignore the offset. Non-finite input becomes 0.
    /// </summary>
    public static double Snap(double seconds, TimelineSnapMode mode, double bpm, double beatOffsetSeconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return 0;
        }

        var step = StepSeconds(mode, bpm);
        var phase = mode is TimelineSnapMode.Beat or TimelineSnapMode.Bar ? PositiveModulo(beatOffsetSeconds, step) : 0;
        var snapped = (Math.Round((seconds - phase) / step, MidpointRounding.AwayFromZero) * step) + phase;

        // Show time 0 is a snap point too: it wins when it is nearer than the first grid line (the line before 0
        // that rounding may pick does not exist).
        if (snapped < 0 || seconds < snapped - seconds)
        {
            return 0;
        }

        // Strip floating-point noise (0.30000000000000004) so values show cleanly in the editor.
        return Math.Round(snapped, 6);
    }

    /// <summary>
    /// The grid origin: the first bar line at or after show time 0 for this offset, in [0, bar length). Huge offsets are
    /// folded into one bar so beat times stay precise. Beat index 0 is this line.
    /// </summary>
    public static double GridOrigin(double bpm, double beatOffsetSeconds) =>
        PositiveModulo(beatOffsetSeconds, BarSeconds(bpm));

    /// <summary>
    /// Index of the beat at or before <paramref name="seconds"/>, counted from <see cref="GridOrigin"/> (index 0, a bar
    /// line). Beats between show time 0 and the origin have negative indexes. Non-finite input counts as 0.
    /// </summary>
    public static long BeatIndexAt(double seconds, double bpm, double beatOffsetSeconds)
    {
        if (!double.IsFinite(seconds))
        {
            seconds = 0;
        }

        var index = Math.Floor((seconds - GridOrigin(bpm, beatOffsetSeconds)) / BeatSeconds(bpm));
        return ClampToLong(index);
    }

    /// <summary>Show time of beat <paramref name="beatIndex"/> (see <see cref="BeatIndexAt"/>).</summary>
    public static double BeatTime(long beatIndex, double bpm, double beatOffsetSeconds) =>
        GridOrigin(bpm, beatOffsetSeconds) + (beatIndex * BeatSeconds(bpm));

    /// <summary>True when beat <paramref name="beatIndex"/> starts a bar.</summary>
    public static bool IsBarStart(long beatIndex) => beatIndex % BeatsPerBar == 0;

    /// <summary>
    /// Time of the first beat line at or after <paramref name="seconds"/> (and at or after show time 0). Non-finite
    /// input counts as 0.
    /// </summary>
    public static double FirstBeatAtOrAfter(double seconds, double bpm, double beatOffsetSeconds)
    {
        var index = FirstBeatIndexAtOrAfter(seconds, bpm, beatOffsetSeconds, 1);
        return BeatTime(index, bpm, beatOffsetSeconds);
    }

    /// <summary>
    /// The beat lines from <paramref name="fromSeconds"/> to <paramref name="toSeconds"/> (inclusive; never before show
    /// time 0) for drawing the grid. Allocation-free: use with <c>foreach</c>. <paramref name="beatStride"/> &gt; 1 keeps
    /// only every n-th beat counted from the grid origin (pass <see cref="BeatsPerBar"/> for bar lines only when zoomed
    /// out). At most <see cref="MaxBeatLines"/> lines are produced.
    /// </summary>
    public static BeatLineRange BeatLines(double fromSeconds, double toSeconds, double bpm, double beatOffsetSeconds, int beatStride = 1)
    {
        var stride = Math.Max(1, beatStride);
        if (!double.IsFinite(toSeconds) || toSeconds < 0)
        {
            return default;
        }

        var first = FirstBeatIndexAtOrAfter(fromSeconds, bpm, beatOffsetSeconds, stride);
        return new BeatLineRange(first, stride, toSeconds, GridOrigin(bpm, beatOffsetSeconds), BeatSeconds(bpm));
    }

    private static long FirstBeatIndexAtOrAfter(double seconds, double bpm, double beatOffsetSeconds, int stride)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var origin = GridOrigin(bpm, beatOffsetSeconds);
        var beat = BeatSeconds(bpm);

        // Small tolerance so a time that is a beat line up to floating-point noise yields that line, not the next one.
        var index = ClampToLong(Math.Ceiling(((seconds - origin) / beat) - 1e-9));
        var remainder = index % stride;
        if (remainder != 0)
        {
            index += remainder > 0 ? stride - remainder : -remainder;
        }

        // Lines before show time 0 do not exist.
        while (origin + (index * beat) < 0)
        {
            index += stride;
        }

        return index;
    }

    /// <summary><paramref name="value"/> mod <paramref name="modulus"/> in [0, modulus); non-finite values give 0.</summary>
    private static double PositiveModulo(double value, double modulus)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        var result = value % modulus;
        if (result < 0)
        {
            result += modulus;
        }

        // -1e-18 + modulus rounds to modulus.
        return result >= modulus ? 0 : result;
    }

    private static long ClampToLong(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        if (value >= long.MaxValue / 2)
        {
            return long.MaxValue / 2;
        }

        return value <= long.MinValue / 2 ? long.MinValue / 2 : (long)value;
    }
}
