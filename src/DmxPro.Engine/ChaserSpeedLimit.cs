namespace DmxPro.Engine;

/// <summary>
/// Speed limit for chasers (safety rule 8). A chaser that changes step fast is a strobe, so every step change is held
/// for at least half a flash period of the allowed flash rate: a step lasts no less than
/// <c>1 / (2 × rate)</c> seconds of real time (one flash = an on step plus an off step). The rate is the global
/// maximum strobe rate while strobe is allowed, and <see cref="SafeFlashRateHz"/> (or the maximum strobe rate when it
/// is lower) while it is not. Applied at play time only; stored step times are never changed.
/// </summary>
public static class ChaserSpeedLimit
{
    /// <summary>
    /// Flash rate a chaser may reach while "allow strobe" is off: 3 flashes per second, the general flash threshold for
    /// photosensitive seizure risk (WCAG 2.3.1, ITU-R BT.1702). Steps are then at least 1/6 s long.
    /// </summary>
    public const double SafeFlashRateHz = 3;

    /// <summary>The flash rate (Hz) chasers may reach with these global strobe settings.</summary>
    /// <param name="allowStrobe">Global "allow strobe" setting.</param>
    /// <param name="maxStrobeRateHz">Global maximum strobe rate; 0, negative or NaN = no strobing at all.</param>
    public static double MaxFlashRateHz(bool allowStrobe, double maxStrobeRateHz)
    {
        var max = double.IsFinite(maxStrobeRateHz) && maxStrobeRateHz > 0 ? maxStrobeRateHz : 0;
        if (allowStrobe && max > 0)
        {
            return max;
        }

        // Strobe not allowed (or a maximum of 0 = "no strobing"): the safe rate, or the user's lower maximum.
        return max > 0 ? Math.Min(max, SafeFlashRateHz) : SafeFlashRateHz;
    }

    /// <summary>Shortest real time (seconds) between two step changes with these global strobe settings.</summary>
    /// <param name="allowStrobe">Global "allow strobe" setting.</param>
    /// <param name="maxStrobeRateHz">Global maximum strobe rate; 0, negative or NaN = no strobing at all.</param>
    public static double MinStepSeconds(bool allowStrobe, double maxStrobeRateHz) =>
        1.0 / (2.0 * MaxFlashRateHz(allowStrobe, maxStrobeRateHz));
}
