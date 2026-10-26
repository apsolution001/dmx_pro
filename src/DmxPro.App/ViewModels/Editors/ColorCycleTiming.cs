using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Pure maths of the colour cycle editor: one round's length, the "Spread across lights" percentage ⇄ the saved
/// per-light offset in seconds, and which colour a light shows at a moment (the on-screen preview). Mirrors the
/// engine's <c>ColorCycleProgram</c>: every colour slot is hold + fade (stretched to the shortest step), the colour
/// holds, then crossfades linearly to the next; the list loops; light i runs i × offset behind the first.
/// </summary>
public static class ColorCycleTiming
{
    /// <summary>Highest "Spread across lights" value: 100 % = the colours are spread evenly across all lights.</summary>
    public const double MaxSpreadPercent = 100;

    /// <summary>Length of one colour slot (hold + fade), at least the engine's shortest step.</summary>
    public static double SlotSeconds(double holdSeconds, double fadeSeconds) =>
        Math.Max(Sanitize(holdSeconds) + Sanitize(fadeSeconds), FunctionEditing.MinStepSeconds);

    /// <summary>Length of one round through all colours; 0 without colours.</summary>
    public static double RoundSeconds(int colorCount, double holdSeconds, double fadeSeconds) =>
        colorCount <= 0 ? 0 : colorCount * SlotSeconds(holdSeconds, fadeSeconds);

    /// <summary>True when spreading means something: at least one colour and two lights.</summary>
    public static bool CanSpread(int colorCount, int lightCount) => colorCount > 0 && lightCount >= 2;

    /// <summary>
    /// Per-light offset (seconds, 3 decimals) for a spread: 0 % = all lights in sync, 100 % = light i starts
    /// i / lights of a round later, so one round of colours is spread evenly across the lights. Values above 100 are
    /// kept (an offset typed in an older version); negative values count as 0.
    /// </summary>
    public static double OffsetForSpread(double spreadPercent, double roundSeconds, int lightCount)
    {
        if (lightCount < 2 || !(roundSeconds > 0) || !double.IsFinite(spreadPercent) || spreadPercent <= 0)
        {
            return 0;
        }

        return FunctionEditing.Clamp(spreadPercent / 100 * roundSeconds / lightCount, 0, FunctionEditing.MaxSeconds);
    }

    /// <summary>The spread percentage a saved offset means (may be above 100 for offsets typed in older versions); 0 when undefined.</summary>
    public static double SpreadForOffset(double offsetSeconds, double roundSeconds, int lightCount)
    {
        if (lightCount < 2 || !(roundSeconds > 0) || !double.IsFinite(offsetSeconds) || offsetSeconds <= 0)
        {
            return 0;
        }

        return offsetSeconds * lightCount / roundSeconds * 100;
    }

    /// <summary>The colour shown <paramref name="seconds"/> into the cycle (negative = before the start, wraps).</summary>
    public static RgbColor ColorAt(IReadOnlyList<RgbColor> colors, double holdSeconds, double fadeSeconds, double seconds)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var count = colors.Count;
        if (count == 0)
        {
            return default;
        }

        var fade = Sanitize(fadeSeconds);
        var slot = SlotSeconds(holdSeconds, fadeSeconds);
        var hold = slot - fade;
        var round = count * slot;
        var position = double.IsFinite(seconds) ? seconds % round : 0;
        if (position < 0)
        {
            position += round;
        }

        var index = Math.Min((int)(position / slot), count - 1);
        var within = position - (index * slot);
        var color = colors[index];
        if (fade <= 0 || within <= hold || count < 2)
        {
            return color;
        }

        var next = colors[(index + 1) % count];
        var p = Math.Min(1, (within - hold) / fade);
        return new RgbColor(Mix(color.R, next.R, p), Mix(color.G, next.G, p), Mix(color.B, next.B, p));
    }

    /// <summary>The colour each light shows at the moment the cycle starts (light i is i × offset behind).</summary>
    public static IReadOnlyList<RgbColor> LightColorsAtStart(
        IReadOnlyList<RgbColor> colors, double holdSeconds, double fadeSeconds, double offsetSeconds, int lightCount)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var result = new RgbColor[Math.Max(lightCount, 0)];
        if (colors.Count == 0)
        {
            return result;
        }

        var offset = double.IsFinite(offsetSeconds) ? offsetSeconds : 0;
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ColorAt(colors, holdSeconds, fadeSeconds, -i * offset);
        }

        return result;
    }

    private static double Sanitize(double seconds) =>
        double.IsFinite(seconds) ? Math.Clamp(seconds, 0, FunctionEditing.MaxSeconds) : 0;

    private static byte Mix(byte from, byte to, double p) => (byte)Math.Clamp(Math.Round(from + ((to - from) * p)), 0, 255);
}
