namespace DmxPro.Engine;

/// <summary>
/// Priority layer a running function belongs to. A higher layer wins on the channels it touches; when it releases, the
/// lower layers show again (owner decision 2026-10-17: show → console → preview → manual overrides).
/// </summary>
public enum EngineLayer
{
    /// <summary>Show timeline playback (lowest priority).</summary>
    Show = 0,

    /// <summary>Functions started from the Virtual Console; override a running show.</summary>
    Console = 1,

    /// <summary>
    /// The one function being checked: the open editor's draft (live edit preview), shown automatically while live mode
    /// is off. Emptied and refused in live mode.
    /// </summary>
    Preview = 2,
}
