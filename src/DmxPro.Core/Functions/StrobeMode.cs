namespace DmxPro.Core.Functions;

/// <summary>How a <see cref="StrobeFunction"/> makes the light flash.</summary>
public enum StrobeMode
{
    /// <summary>
    /// The engine switches the colour/dimmer on and off at <see cref="StrobeFunction.RateHz"/>. Works on every fixture
    /// and the rate is exact, so the global maximum strobe rate is enforced precisely.
    /// </summary>
    Software,

    /// <summary>
    /// Uses the fixture's own Strobe channel (profile-declared) set to <see cref="StrobeFunction.HardwareValue"/>, with
    /// the light on. Fixtures without a Strobe channel fall back to <see cref="Software"/>. The fixture decides the real
    /// rate (profiles carry no value → Hz data), so only "allow strobe" governs it.
    /// </summary>
    Hardware,
}
