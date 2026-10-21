namespace DmxPro.Engine;

/// <summary>
/// Where a show was started from. Live mode (<see cref="DmxEngine.SetLiveMode"/>) lets only the Virtual Console drive
/// the lights, so shows started from an editing screen are stopped when live mode turns on and cannot start while it is
/// on.
/// </summary>
public enum ShowOrigin
{
    /// <summary>Started from the Virtual Console (button, show player). Plays in live mode.</summary>
    Console = 0,

    /// <summary>Started from an editing screen (the Shows page transport). Suppressed in live mode.</summary>
    Editor = 1,
}
