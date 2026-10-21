namespace DmxPro.App.Services;

/// <summary>
/// What the Virtual Console needs from playback. The console only calls these members; it never touches DMX buffers.
/// Levels are 0..1 (clamped by the implementation). Blackout is a separate dark state and is NOT "output disabled"
/// (safety rule 3). <see cref="StateChanged"/> may be raised on any thread; the console marshals it to the UI thread.
/// </summary>
public interface IConsolePlayback
{
    /// <summary>Raised whenever any active/running/level state changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Grand master 0..1 (1 = full).</summary>
    double GrandMaster { get; }

    /// <summary>True while blackout is on (output keeps sending a dark frame).</summary>
    bool IsBlackout { get; }

    /// <summary>Starts the function if it is off, stops it if it is on.</summary>
    void ToggleFunction(Guid functionId);

    /// <summary>Starts a momentary (flash) activation; must be paired with <see cref="FlashUp"/>.</summary>
    void FlashDown(Guid functionId);

    /// <summary>Ends a momentary activation started by <see cref="FlashDown"/>.</summary>
    void FlashUp(Guid functionId);

    /// <summary>Runs the function at an intensity level 0..1 (0 = stopped). Used when buttons are shown as sliders.</summary>
    void SetFunctionLevel(Guid functionId, double level);

    /// <summary>Starts the show if it is stopped, stops it if it is running.</summary>
    void ToggleShow(Guid showId);

    /// <summary>Sets a group intensity level 0..1.</summary>
    void SetGroupLevel(Guid groupId, double level);

    void SetGrandMaster(double level);

    /// <summary>Turns blackout on or off. Never disables output.</summary>
    void SetBlackout(bool on);

    /// <summary>Stops every function and show, ends flashes and returns levels to defaults (safety rule 4).</summary>
    void ReleaseAll();

    /// <summary>Sets a speed factor (1 = normal, 0.25..4) for one function, or for all functions when null.</summary>
    void SetSpeed(Guid? functionId, double factor);

    bool IsFunctionActive(Guid functionId);

    double GetFunctionLevel(Guid functionId);

    bool IsShowRunning(Guid showId);

    double GetGroupLevel(Guid groupId);

    double GetSpeed(Guid? functionId);

    // ---- Show player (Virtual Console "Shows" page). Only one show plays at a time from the player. ----

    /// <summary>
    /// True while a show is playing but muted because at least one console function is active (owner decision
    /// 2026-10-18: any console function = override; the show's light output is fully muted, its timeline keeps running).
    /// </summary>
    bool IsShowOverridden { get; }

    /// <summary>Plays the show: resumes when paused, starts from <paramref name="fromSeconds"/> (default 0) when stopped.
    /// Starting a show stops any other show started from the player.</summary>
    /// <param name="showId">Show to play.</param>
    /// <param name="fromSeconds">Start position when the show is stopped.</param>
    /// <param name="origin">
    /// Who asks: <see cref="DmxPro.Engine.ShowOrigin.Console"/> (Virtual Console, default) or
    /// <see cref="DmxPro.Engine.ShowOrigin.Editor"/> (the Shows page transport). While live mode is on
    /// (<see cref="ILiveModeService"/>) an editor request is refused (nothing starts or resumes; <see cref="StateChanged"/>
    /// is still raised so the UI shows the real state), and turning live mode on stops every editor show.
    /// </param>
    void PlayShow(Guid showId, double fromSeconds = 0, DmxPro.Engine.ShowOrigin origin = DmxPro.Engine.ShowOrigin.Console);

    /// <summary>Pauses the show: the timeline position freezes and the show's lights hold their current look.</summary>
    void PauseShow(Guid showId);

    /// <summary>Stops the show and releases its lights (position returns to 0).</summary>
    void StopShow(Guid showId);

    /// <summary>Jumps to <paramref name="positionSeconds"/> (clamped to 0..length). Keeps playing/paused state.</summary>
    void SeekShow(Guid showId, double positionSeconds);

    bool IsShowPaused(Guid showId);

    /// <summary>Current timeline position in seconds (0 when stopped). Poll it from a UI timer (~30 Hz);
    /// <see cref="StateChanged"/> is NOT raised for every position change.</summary>
    double GetShowPosition(Guid showId);

    // ---- Show music sync (used by IShowMusicService and the transport controls). ----

    /// <summary>
    /// Raised after every show transport command (start, pause, resume, seek, stop, Release All, project reset), when a
    /// show ended by itself, and for scrub begin/end, so the show's music follows it wherever it came from. Raised on
    /// the caller's thread (the poll timer thread for <see cref="ShowTransportKind.Ended"/>), outside any lock, before
    /// <see cref="StateChanged"/>. Not raised for <see cref="SyncShowPosition"/>.
    /// </summary>
    event EventHandler<ShowTransportEventArgs>? ShowTransport;

    /// <summary>
    /// The engine's own show clock: the position of the latest engine snapshot and that snapshot's sequence number (a
    /// new number = a fresh snapshot). False when the show is not running or the snapshot does not reflect the last
    /// command for the show yet. Cheap; for a ~40 Hz sync timer.
    /// </summary>
    bool TryGetShowClock(Guid showId, out double positionSeconds, out long snapshotSequence);

    /// <summary>
    /// Moves a running show's lights to <paramref name="positionSeconds"/> to follow its music (drift correction).
    /// Same as <see cref="SeekShow"/> for the engine, but raises neither <see cref="ShowTransport"/> nor
    /// <see cref="StateChanged"/>.
    /// </summary>
    void SyncShowPosition(Guid showId, double positionSeconds);

    /// <summary>
    /// The user starts scrubbing / holding fast-forward or rewind on a running show: its music goes silent (lights
    /// still follow every seek). Pair every call with <see cref="EndShowScrub"/>.
    /// </summary>
    void BeginShowScrub(Guid showId);

    /// <summary>Ends <see cref="BeginShowScrub"/>: the music rejoins the show in sync at its current position.</summary>
    void EndShowScrub(Guid showId);
}
