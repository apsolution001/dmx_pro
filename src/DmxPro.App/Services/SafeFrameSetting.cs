namespace DmxPro.App.Services;

/// <summary>The one frame sent to every universe on application close before output stops (safety rule 6).</summary>
public enum SafeFrameSetting
{
    /// <summary>All channels 0.</summary>
    Blackout,

    /// <summary>Every channel at its profile default value (e.g. a dimmer-less fixture's shutter "open").</summary>
    ProfileDefaults,
}
