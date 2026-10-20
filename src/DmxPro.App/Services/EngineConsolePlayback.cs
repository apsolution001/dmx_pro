using DmxPro.Engine;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.Services;

/// <summary>
/// Engine-backed <see cref="IConsolePlayback"/>. Console functions play in <see cref="EngineLayer.Console"/>; shows in the
/// show layer. What the console commanded is tracked here so the UI sees it at once (engine commands apply at the next
/// tick); the engine is the authority for show positions and for shows ending by themselves, which a background poll
/// picks up and reports through <see cref="StateChanged"/>.
/// </summary>
/// <remarks>
/// Thread-safe: members may be called from any thread; state is guarded by one lock and <see cref="StateChanged"/> is
/// raised outside it (from the caller's thread, or from the poll timer thread when a show ended). Every show transport
/// change also raises <see cref="ShowTransport"/> (before <see cref="StateChanged"/>) so the show music follows it.
/// </remarks>
public sealed class EngineConsolePlayback : IConsolePlayback, IDisposable
{
    /// <summary>How often the poll looks for shows that ended on their own.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly DmxEngine _engine;
    private readonly ILogger<EngineConsolePlayback> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, double> _functionLevels = [];
    private readonly HashSet<Guid> _flashes = [];

    // Intensity last sent to the engine per console function; only entries > 0 (= running in the engine) are kept.
    private readonly Dictionary<Guid, double> _sent = [];
    private readonly Dictionary<Guid, ShowState> _shows = [];
    private readonly Dictionary<Guid, double> _groupLevels = [];
    private readonly Dictionary<Guid, double> _speeds = [];
    private readonly Timer _poll;
    private double _globalSpeed = 1;

    public EngineConsolePlayback(DmxEngine engine, ILogger<EngineConsolePlayback> logger)
    {
        _engine = engine;
        _logger = logger;
        _poll = new Timer(_ => PollShows(), null, PollInterval, PollInterval);
    }

    public event EventHandler? StateChanged;

    public event EventHandler<ShowTransportEventArgs>? ShowTransport;

    /// <summary>
    /// Raised after <see cref="ReleaseAll"/> queued the engine Release All (which also clears every manual override),
    /// so holders of overrides (the Test page) can forget theirs. Raised on the caller's thread.
    /// </summary>
    public event EventHandler? AllReleased;

    public double GrandMaster => _engine.GrandMaster;

    public bool IsBlackout => _engine.Blackout;

    public bool IsShowOverridden
    {
        get
        {
            lock (_gate)
            {
                return _engine.ShowOverrideMode == ShowOverrideMode.MuteWholeShow && _shows.Count > 0 && _sent.Count > 0;
            }
        }
    }

    public void ToggleFunction(Guid functionId)
    {
        lock (_gate)
        {
            var level = Level(functionId) > 0 ? 0 : 1;
            if (level > 0 && !CanStart(functionId))
            {
                return;
            }

            _functionLevels[functionId] = level;
            ApplyFunction(functionId);
        }

        Changed();
    }

    public void FlashDown(Guid functionId)
    {
        lock (_gate)
        {
            if (!CanStart(functionId) || !_flashes.Add(functionId))
            {
                return;
            }

            ApplyFunction(functionId);
        }

        Changed();
    }

    public void FlashUp(Guid functionId)
    {
        lock (_gate)
        {
            if (!_flashes.Remove(functionId))
            {
                return;
            }

            ApplyFunction(functionId);
        }

        Changed();
    }

    public void SetFunctionLevel(Guid functionId, double level)
    {
        level = Clamp01(level);
        lock (_gate)
        {
            // Not playable: keep it at 0 and still notify, so a dragged slider snaps back to the real state.
            if (level > 0 && !CanStart(functionId))
            {
                level = 0;
            }

            _functionLevels[functionId] = level;
            ApplyFunction(functionId);
        }

        Changed();
    }

    public void ToggleShow(Guid showId)
    {
        ShowTransportEventArgs? transport = null;
        lock (_gate)
        {
            if (_shows.Remove(showId))
            {
                _engine.StopShow(showId);
                transport = new ShowTransportEventArgs(ShowTransportKind.Stopped, showId);
            }
            else if (StartShowLocked(showId, 0, fromPlayer: false))
            {
                transport = new ShowTransportEventArgs(ShowTransportKind.Started, showId, 0);
            }
        }

        Transport(transport);
        Changed();
    }

    public void PlayShow(Guid showId, double fromSeconds = 0)
    {
        List<Guid>? stopped = null;
        ShowTransportEventArgs? transport = null;
        lock (_gate)
        {
            if (_shows.TryGetValue(showId, out var state))
            {
                if (state.Paused)
                {
                    _engine.ResumeShow(showId);
                    state.Paused = false;
                    state.FromPlayer = true;
                    Confirm(state, state.PendingPosition);
                    transport = new ShowTransportEventArgs(ShowTransportKind.Resumed, showId, state.PendingPosition);
                }
            }
            else
            {
                stopped = StopOtherPlayerShowsLocked(showId);
                var position = StartPosition(fromSeconds);
                if (StartShowLocked(showId, position, fromPlayer: true))
                {
                    transport = new ShowTransportEventArgs(ShowTransportKind.Started, showId, position);
                }
            }
        }

        if (stopped is not null)
        {
            foreach (var id in stopped)
            {
                Transport(new ShowTransportEventArgs(ShowTransportKind.Stopped, id));
            }
        }

        Transport(transport);
        Changed();
    }

    public void PauseShow(Guid showId)
    {
        double position;
        lock (_gate)
        {
            if (!_shows.TryGetValue(showId, out var state) || state.Paused)
            {
                return;
            }

            position = PositionLocked(showId, state);
            _engine.PauseShow(showId);
            state.Paused = true;
            Confirm(state, position);
        }

        Transport(new ShowTransportEventArgs(ShowTransportKind.Paused, showId, position));
        Changed();
    }

    public void StopShow(Guid showId)
    {
        lock (_gate)
        {
            if (!_shows.Remove(showId))
            {
                return;
            }

            _engine.StopShow(showId);
        }

        Transport(new ShowTransportEventArgs(ShowTransportKind.Stopped, showId));
        Changed();
    }

    public void SeekShow(Guid showId, double positionSeconds)
    {
        double position;
        lock (_gate)
        {
            if (!SeekLocked(showId, positionSeconds, out position))
            {
                return;
            }
        }

        Transport(new ShowTransportEventArgs(ShowTransportKind.Seeked, showId, position));
        Changed();
    }

    public void SyncShowPosition(Guid showId, double positionSeconds)
    {
        lock (_gate)
        {
            SeekLocked(showId, positionSeconds, out _);
        }
    }

    public bool TryGetShowClock(Guid showId, out double positionSeconds, out long snapshotSequence)
    {
        lock (_gate)
        {
            if (_shows.TryGetValue(showId, out var state)
                && _engine.TryGetShowPlayback(showId, out var info, out snapshotSequence)
                && snapshotSequence >= state.ConfirmedFromSequence)
            {
                state.LengthSeconds = info.Length.TotalSeconds;
                positionSeconds = info.Position.TotalSeconds;
                return true;
            }
        }

        positionSeconds = 0;
        snapshotSequence = -1;
        return false;
    }

    public void BeginShowScrub(Guid showId)
    {
        if (IsShowRunning(showId))
        {
            Transport(new ShowTransportEventArgs(ShowTransportKind.ScrubStarted, showId, GetShowPosition(showId)));
        }
    }

    public void EndShowScrub(Guid showId) =>
        Transport(new ShowTransportEventArgs(ShowTransportKind.ScrubEnded, showId, GetShowPosition(showId)));

    public bool IsShowPaused(Guid showId)
    {
        lock (_gate)
        {
            return _shows.TryGetValue(showId, out var state) && state.Paused;
        }
    }

    public double GetShowPosition(Guid showId)
    {
        lock (_gate)
        {
            return _shows.TryGetValue(showId, out var state) ? PositionLocked(showId, state) : 0;
        }
    }

    public void SetGroupLevel(Guid groupId, double level)
    {
        // TODO(engine): group intensity masters are not in the engine yet; the level is tracked for the console UI only.
        lock (_gate)
        {
            _groupLevels[groupId] = Clamp01(level);
        }

        Changed();
    }

    public void SetGrandMaster(double level)
    {
        _engine.GrandMaster = level;
        Changed();
    }

    public void SetBlackout(bool on)
    {
        _engine.Blackout = on;
        Changed();
    }

    public void ReleaseAll()
    {
        // Engine Release All: every function, show, preview and manual override stops at the next tick (safety rule 4).
        // Grand Master and Blackout are operator safety controls and stay as they are.
        _engine.ReleaseAll();
        lock (_gate)
        {
            ClearLocked();
        }

        AllReleased?.Invoke(this, EventArgs.Empty);
        Transport(new ShowTransportEventArgs(ShowTransportKind.AllStopped, Guid.Empty));
        Changed();
    }

    public void SetSpeed(Guid? functionId, double factor)
    {
        // A function's own speed is multiplied by the global speed; the engine speeds up the function's own time only.
        var clamped = double.IsFinite(factor) ? Math.Clamp(factor, 0.25, 4) : 1;
        lock (_gate)
        {
            if (functionId is { } id)
            {
                _speeds[id] = clamped;
                if (_sent.ContainsKey(id))
                {
                    _engine.SetFunctionSpeed(id, EngineLayer.Console, EffectiveSpeed(id));
                }
            }
            else
            {
                _globalSpeed = clamped;
                foreach (var running in _sent.Keys)
                {
                    _engine.SetFunctionSpeed(running, EngineLayer.Console, EffectiveSpeed(running));
                }
            }
        }

        Changed();
    }

    public bool IsFunctionActive(Guid functionId)
    {
        lock (_gate)
        {
            return _flashes.Contains(functionId) || Level(functionId) > 0;
        }
    }

    public double GetFunctionLevel(Guid functionId)
    {
        lock (_gate)
        {
            return Level(functionId);
        }
    }

    public bool IsShowRunning(Guid showId)
    {
        lock (_gate)
        {
            return _shows.ContainsKey(showId);
        }
    }

    public double GetGroupLevel(Guid groupId)
    {
        lock (_gate)
        {
            return _groupLevels.TryGetValue(groupId, out var level) ? level : 0;
        }
    }

    public double GetSpeed(Guid? functionId)
    {
        lock (_gate)
        {
            return functionId is { } id ? (_speeds.TryGetValue(id, out var speed) ? speed : 1) : _globalSpeed;
        }
    }

    /// <summary>
    /// A different project was loaded into the engine (which releases everything): forget all console state. Grand
    /// Master and Blackout are kept.
    /// </summary>
    public void ResetAfterProjectLoad()
    {
        lock (_gate)
        {
            ClearLocked();
        }

        Transport(new ShowTransportEventArgs(ShowTransportKind.AllStopped, Guid.Empty));
        Changed();
    }

    /// <summary>
    /// The engine re-compiled the edited project: functions/shows that no longer exist (or can no longer play) were
    /// stopped by the engine, so drop them here too.
    /// </summary>
    public void SyncWithModel()
    {
        var changed = false;
        List<Guid>? removedShows = null;
        lock (_gate)
        {
            foreach (var id in _sent.Keys.ToList())
            {
                if (!_engine.CanPlay(id))
                {
                    _sent.Remove(id);
                    _functionLevels.Remove(id);
                    _flashes.Remove(id);
                    changed = true;
                }
            }

            foreach (var id in _shows.Keys.ToList())
            {
                if (!_engine.CanPlayShow(id))
                {
                    _shows.Remove(id);
                    (removedShows ??= []).Add(id);
                    changed = true;
                }
            }
        }

        if (removedShows is not null)
        {
            foreach (var id in removedShows)
            {
                Transport(new ShowTransportEventArgs(ShowTransportKind.Stopped, id));
            }
        }

        if (changed)
        {
            Changed();
        }
    }

    public void Dispose() => _poll.Dispose();

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private double EffectiveSpeed(Guid functionId) =>
        (_speeds.TryGetValue(functionId, out var speed) ? speed : 1) * _globalSpeed;

    private double Level(Guid functionId) => _functionLevels.TryGetValue(functionId, out var level) ? level : 0;

    private bool CanStart(Guid functionId)
    {
        if (_engine.CanPlay(functionId))
        {
            return true;
        }

        _logger.LogWarning("Console function {FunctionId} cannot be played (missing or not playable)", functionId);
        return false;
    }

    /// <summary>Sends the function's effective console intensity (flash = full, else its level) to the engine.</summary>
    private void ApplyFunction(Guid functionId)
    {
        var target = _flashes.Contains(functionId) ? 1.0 : Level(functionId);
        var running = _sent.TryGetValue(functionId, out var current);
        if (target > 0)
        {
            if (!running)
            {
                _engine.StartFunction(functionId, EngineLayer.Console, target);
                var speed = EffectiveSpeed(functionId);
                if (speed != 1)
                {
                    _engine.SetFunctionSpeed(functionId, EngineLayer.Console, speed);
                }
            }
            else if (target != current)
            {
                // Intensity only: keeps the function's LTP order (a slider drag must not re-take channels).
                _engine.SetFunctionIntensity(functionId, EngineLayer.Console, target);
            }

            _sent[functionId] = target;
        }
        else if (running)
        {
            _engine.StopFunction(functionId, EngineLayer.Console);
            _sent.Remove(functionId);
        }

        if (target <= 0 && !_flashes.Contains(functionId))
        {
            _functionLevels.Remove(functionId);
        }
    }

    private static double StartPosition(double fromSeconds) => double.IsFinite(fromSeconds) ? Math.Max(0, fromSeconds) : 0;

    private bool StartShowLocked(Guid showId, double fromSeconds, bool fromPlayer)
    {
        if (!_engine.CanPlayShow(showId))
        {
            _logger.LogWarning("Show {ShowId} cannot be played: not found", showId);
            return false;
        }

        var position = StartPosition(fromSeconds);
        _engine.StartShow(showId, TimeSpan.FromSeconds(position));
        var state = new ShowState { FromPlayer = fromPlayer };
        Confirm(state, position);
        _shows[showId] = state;
        return true;
    }

    private List<Guid>? StopOtherPlayerShowsLocked(Guid keep)
    {
        List<Guid>? stopped = null;
        foreach (var (id, state) in _shows.ToList())
        {
            if (id != keep && state.FromPlayer)
            {
                _shows.Remove(id);
                _engine.StopShow(id);
                (stopped ??= []).Add(id);
            }
        }

        return stopped;
    }

    /// <summary>Seeks a running show (clamped to 0..length when the length is known). False when it is not running.</summary>
    private bool SeekLocked(Guid showId, double positionSeconds, out double position)
    {
        position = 0;
        if (!_shows.TryGetValue(showId, out var state))
        {
            return false;
        }

        position = double.IsFinite(positionSeconds) ? Math.Max(0, positionSeconds) : 0;
        if (state.LengthSeconds is { } length)
        {
            position = Math.Min(position, length);
        }

        _engine.SeekShow(showId, TimeSpan.FromSeconds(position));
        Confirm(state, position);
        return true;
    }

    /// <summary>
    /// Remembers what was just commanded: until a snapshot taken after the command arrives, the commanded position is
    /// reported (engine commands apply at the next tick; see <see cref="DmxEngine.TickCount"/>).
    /// </summary>
    private void Confirm(ShowState state, double position)
    {
        state.PendingPosition = position;
        state.ConfirmedFromSequence = _engine.TickCount + 2;
    }

    private double PositionLocked(Guid showId, ShowState state)
    {
        if (_engine.TryGetShowPlayback(showId, out var info, out var sequence) && sequence >= state.ConfirmedFromSequence)
        {
            state.LengthSeconds = info.Length.TotalSeconds;
            return info.Position.TotalSeconds;
        }

        // Not confirmed yet: what we asked for. Confirmed but gone: it ended; the poll will drop it.
        return sequence >= state.ConfirmedFromSequence ? 0 : state.PendingPosition;
    }

    private void PollShows()
    {
        List<Guid>? ended = null;
        try
        {
            lock (_gate)
            {
                if (_shows.Count == 0)
                {
                    return;
                }

                foreach (var (id, state) in _shows)
                {
                    var found = _engine.TryGetShowPlayback(id, out _, out var sequence);
                    if (!found && sequence >= state.ConfirmedFromSequence)
                    {
                        (ended ??= []).Add(id);
                    }
                }

                if (ended is not null)
                {
                    foreach (var id in ended)
                    {
                        _shows.Remove(id);
                    }
                }
            }

            if (ended is not null)
            {
                foreach (var id in ended)
                {
                    Transport(new ShowTransportEventArgs(ShowTransportKind.Ended, id));
                }

                Changed();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Polling show playback failed");
        }
    }

    private void ClearLocked()
    {
        _functionLevels.Clear();
        _flashes.Clear();
        _sent.Clear();
        _shows.Clear();
        _groupLevels.Clear();
        _speeds.Clear();
        _globalSpeed = 1;
    }

    private void Changed() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void Transport(ShowTransportEventArgs? args)
    {
        if (args is not null)
        {
            ShowTransport?.Invoke(this, args);
        }
    }

    private sealed class ShowState
    {
        public bool Paused { get; set; }

        /// <summary>Started from the show player (only one of those plays at a time).</summary>
        public bool FromPlayer { get; set; }

        public double PendingPosition { get; set; }

        /// <summary>First snapshot sequence that reflects the last command for this show.</summary>
        public long ConfirmedFromSequence { get; set; }

        /// <summary>Show length once seen in a snapshot (for clamping seeks).</summary>
        public double? LengthSeconds { get; set; }
    }
}
