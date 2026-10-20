namespace DmxPro.Engine;

/// <summary>Why a strobe effect is not flashing as programmed (safety rule 8). Reported in snapshots.</summary>
[Flags]
public enum StrobeLimit
{
    /// <summary>Strobing as programmed (or not a strobe).</summary>
    None = 0,

    /// <summary>"Allow strobe" is off (or the maximum strobe rate is 0): the effect shows steady light instead.</summary>
    Suppressed = 1,

    /// <summary>The requested rate (× speed) is above the maximum strobe rate and plays at the maximum instead.</summary>
    RateLimited = 2,
}
