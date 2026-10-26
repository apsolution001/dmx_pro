using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Curves of the on-screen previews in the pulse and strobe editors. Pure maths (no WPF), so it is unit-tested. The
/// pulse curve uses the same formula as the engine (<c>PulseProgram.LevelAt</c>), so the picture matches the lights.
/// </summary>
public static class EffectPreviewMath
{
    /// <summary>Points of a pulse preview (two breaths).</summary>
    public const int PulseSamples = 97;

    /// <summary>Breaths shown side by side in the pulse preview.</summary>
    public const int PulseCyclesShown = 2;

    /// <summary>
    /// Shortest breath the screen preview animates. Faster pulses are shown as a still picture: more than two
    /// brightness changes a second on screen could disturb photosensitive people (safety rule 8 spirit).
    /// </summary>
    public const double MinAnimatedPeriodSeconds = 0.5;

    /// <summary>The strobe preview shows this much time (one second).</summary>
    public const double StrobeWindowSeconds = 1.0;

    /// <summary>Pulse level 0..1 at <paramref name="phase"/> 0..1 of one breath (starts at the minimum).</summary>
    public static double PulseLevel(PulseShape shape, double phase, double minLevel, double maxLevel)
    {
        var p = double.IsFinite(phase) ? phase - Math.Floor(phase) : 0;
        var curve = shape == PulseShape.Triangle
            ? (p < 0.5 ? 2 * p : 2 - (2 * p))
            : 0.5 - (0.5 * Math.Cos(2 * Math.PI * p));
        var min = Clamp01(minLevel);
        var max = Clamp01(maxLevel);
        return Clamp01(min + ((max - min) * curve));
    }

    /// <summary><see cref="PulseCyclesShown"/> breaths as evenly spaced points across the preview.</summary>
    public static IReadOnlyList<EffectPreviewPoint> PulseCurve(PulseShape shape, double minLevel, double maxLevel)
    {
        var points = new EffectPreviewPoint[PulseSamples];
        for (var i = 0; i < PulseSamples; i++)
        {
            var x = i / (double)(PulseSamples - 1);
            points[i] = new EffectPreviewPoint(x, PulseLevel(shape, x * PulseCyclesShown, minLevel, maxLevel));
        }

        return points;
    }

    /// <summary>Whether the screen preview may animate a pulse of this period (see <see cref="MinAnimatedPeriodSeconds"/>).</summary>
    public static bool CanAnimatePulse(double periodSeconds) =>
        double.IsFinite(periodSeconds) && periodSeconds >= MinAnimatedPeriodSeconds;

    /// <summary>
    /// Flashes per second the lights really make: 0 (steady light) when strobe is not allowed in Settings, otherwise
    /// the rate capped at the Settings maximum — the same rules the engine applies.
    /// </summary>
    public static double EffectiveStrobeRate(double rateHz, bool allowStrobe, int maxRateHz)
    {
        if (!allowStrobe || !double.IsFinite(rateHz) || rateHz <= 0)
        {
            return 0;
        }

        return Math.Min(rateHz, Math.Max(0, maxRateHz));
    }

    /// <summary>
    /// One second of strobe as a step curve: on for <paramref name="dutyCycle"/> of each flash. A rate of 0 means a
    /// steady light (strobe not allowed) and gives a flat line at full.
    /// </summary>
    public static IReadOnlyList<EffectPreviewPoint> StrobeCurve(double effectiveRateHz, double dutyCycle)
    {
        if (!double.IsFinite(effectiveRateHz) || effectiveRateHz <= 0)
        {
            return [new EffectPreviewPoint(0, 1), new EffectPreviewPoint(1, 1)];
        }

        var duty = Math.Clamp(double.IsFinite(dutyCycle) ? dutyCycle : StrobeFunction.DefaultDutyCycle, StrobeFunction.MinDutyCycle, StrobeFunction.MaxDutyCycle);
        var flashWidth = 1.0 / (effectiveRateHz * StrobeWindowSeconds);
        var points = new List<EffectPreviewPoint>();
        var flashes = (int)Math.Ceiling((1.0 / flashWidth) - 1e-9);
        for (var k = 0; k < flashes; k++)
        {
            var start = k * flashWidth;
            var off = Math.Min(1.0, start + (flashWidth * duty));
            points.Add(new EffectPreviewPoint(start, 0));
            points.Add(new EffectPreviewPoint(start, 1));
            points.Add(new EffectPreviewPoint(off, 1));
            points.Add(new EffectPreviewPoint(off, 0));
        }

        if (points[^1].X < 1.0)
        {
            points.Add(new EffectPreviewPoint(1.0, 0));
        }

        return points;
    }

    /// <summary>The curve's level at <paramref name="x"/> 0..1 (straight lines between points; 0 for an empty curve).</summary>
    public static double LevelAt(IReadOnlyList<EffectPreviewPoint> points, double x)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return 0;
        }

        if (x <= points[0].X)
        {
            return points[0].Level;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var b = points[i];
            if (x <= b.X)
            {
                var a = points[i - 1];
                var span = b.X - a.X;
                return span <= 0 ? b.Level : a.Level + ((b.Level - a.Level) * ((x - a.X) / span));
            }
        }

        return points[^1].Level;
    }

    /// <summary>The highest level of the curve (0 for an empty curve).</summary>
    public static double PeakLevel(IReadOnlyList<EffectPreviewPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var peak = 0.0;
        foreach (var point in points)
        {
            peak = Math.Max(peak, point.Level);
        }

        return peak;
    }

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
