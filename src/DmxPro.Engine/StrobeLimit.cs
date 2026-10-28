namespace DmxPro.Engine;

/// <summary>Why a function is not playing as fast as programmed (safety rule 8). Reported in snapshots.</summary>
[Flags]
public enum StrobeLimit
{
    /// <summary>Playing as programmed.</summary>
    None = 0,

    /// <summary>
    /// "Allow strobe" is off and a strobe effect shows steady light instead. Kept for compatibility: no engine program
    /// reports it since the strobe function was removed (2026-10-28).
    /// </summary>
    Suppressed = 1,

    /// <summary>
    /// A chaser's steps (× speed) are shorter than <see cref="ChaserSpeedLimit.MinStepSeconds"/> allows and play
    /// stretched to that minimum.
    /// </summary>
    RateLimited = 2,
}
