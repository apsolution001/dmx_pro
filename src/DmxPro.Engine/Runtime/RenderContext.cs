namespace DmxPro.Engine.Runtime;

/// <summary>
/// Per-render settings and results shared by nested programs (a chaser passes it to its steps). One instance, reused
/// by the engine thread every tick; never allocated in the hot path.
/// </summary>
internal sealed class RenderContext
{
    /// <summary>Global "allow strobe" setting for this frame.</summary>
    public bool AllowStrobe { get; set; }

    /// <summary>Global maximum strobe rate for this frame; 0 = no strobing at all.</summary>
    public double MaxStrobeRateHz { get; set; }

    /// <summary>
    /// Speed factor of the function being rendered (its time already runs this much faster). Programs that must cap a
    /// real-time rate (strobe) use it to convert back to real time. 1 for show items.
    /// </summary>
    public double Speed { get; set; } = 1;

    /// <summary>Limits reported by strobe programs during the current render call (reset by the engine).</summary>
    public StrobeLimit StrobeLimit { get; set; }
}
