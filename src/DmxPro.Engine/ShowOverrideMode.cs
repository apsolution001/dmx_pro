namespace DmxPro.Engine;

/// <summary>
/// How functions started from the Virtual Console (the <see cref="EngineLayer.Console"/> layer) interact with shows that
/// are playing (owner decision 2026-10-18).
/// </summary>
public enum ShowOverrideMode
{
    /// <summary>
    /// While at least one console function runs, every playing show is muted on <b>all</b> channels (not only the ones
    /// the console touches). The show timelines keep running, so the show reappears at its current position when the
    /// last console function has released. The default.
    /// </summary>
    MuteWholeShow = 0,

    /// <summary>
    /// The console layer wins only on the channels it touches; the show stays visible everywhere else (plain layer
    /// priority).
    /// </summary>
    TouchedChannelsOnly = 1,
}
