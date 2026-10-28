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
    /// Speed factor of the function being rendered (its time already runs this much faster). Used to convert real-time
    /// limits (the chaser speed limit) to function time. 1 for show items.
    /// </summary>
    public double Speed { get; set; } = 1;

    /// <summary>
    /// Shortest step a chaser may play, in function time (safety rule 8): the real-time minimum from
    /// <see cref="ChaserSpeedLimit.MinStepSeconds"/> × <see cref="Speed"/>. NaN while the speed is 0 or invalid (the
    /// function is paused and nothing changes, so the timeline in use is kept).
    /// </summary>
    public double MinStepSeconds =>
        Speed > 0 && double.IsFinite(Speed) ? ChaserSpeedLimit.MinStepSeconds(AllowStrobe, MaxStrobeRateHz) * Speed : double.NaN;

    /// <summary>Limits reported by programs during the current render call (reset by the engine).</summary>
    public StrobeLimit StrobeLimit { get; set; }
}
