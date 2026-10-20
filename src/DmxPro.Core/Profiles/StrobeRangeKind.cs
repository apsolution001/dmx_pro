namespace DmxPro.Core.Profiles;

/// <summary>What a documented value range on a Strobe/shutter channel does (see <see cref="StrobeCapabilityClassifier"/>).</summary>
public enum StrobeRangeKind
{
    /// <summary>The range's meaning could not be recognised; never treated as confirmed (safety rule 7).</summary>
    Unknown,

    /// <summary>Light shines steadily: shutter open, no strobe, no function.</summary>
    Steady,

    /// <summary>Shutter closed / blackout: the light is dark but not flashing.</summary>
    Closed,

    /// <summary>A strobing effect: strobe, pulse, ramp, random strobe, lightning…</summary>
    Strobing,
}
