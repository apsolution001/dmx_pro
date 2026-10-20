namespace DmxPro.Core.Shows;

/// <summary>How the show editor snaps item edges and selections to time.</summary>
public enum TimelineSnapMode
{
    /// <summary>No snapping (times keep millisecond precision).</summary>
    Off,

    /// <summary>Snap to 0.1 s.</summary>
    Tenth,

    /// <summary>Snap to beats of the show's BPM.</summary>
    Beat,

    /// <summary>Snap to bars (4 beats) of the show's BPM.</summary>
    Bar,
}
