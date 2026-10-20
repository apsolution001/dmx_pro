using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DmxPro.Audio.Analysis;
using DmxPro.Audio.Playback;
using DmxPro.Core.Shows;
using DmxPro.Engine;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.Services;

/// <summary>
/// <see cref="IShowMusicService"/>: plays each running show's song in sync with its lights.
/// </summary>
/// <remarks>
/// <para><b>Following the transport.</b> Every show transport change goes through <see cref="IConsolePlayback"/>, which
/// raises <see cref="IConsolePlayback.ShowTransport"/> (start, pause, resume, seek, stop, end, Release All, project
/// reset, scrub begin/end). This service keeps one <see cref="ShowMusicSession"/> per running show that has unmuted,
/// found music, with its own <see cref="IAudioPlayer"/>. Shows without music, with muted music or with a missing file run
/// on the engine clock alone.</para>
/// <para><b>Threads.</b> All state lives on the UI thread: transport events raised on another thread (the playback poll
/// reporting a show end) are posted to the dispatcher, events raised on the UI thread are handled at once so the music
/// pauses in the same instant as the lights. Player errors arrive on a background thread and are posted too. The sync
/// timer is a <see cref="DispatcherTimer"/> (40 Hz) that runs only while a show with music plays.</para>
/// <para><b>Sync (the music is the master clock).</b> The engine show position is estimated precisely with
/// <see cref="ShowMusicClock"/> (raw snapshot positions lag by up to ~50 ms). Each tick compares the music's show time
/// (<c>offset + player.Position</c>) with the lights; when the median drift over ~250 ms (<see cref="ShowMusicDriftFilter"/>)
/// exceeds 25 ms the lights are moved to the music with <see cref="IConsolePlayback.SyncShowPosition"/>, at most every
/// 0.5 s. The music itself is never sought for small drift. Before the song starts and after it ends the engine clock
/// is master; the song is started up to one tick early and the lights aligned to it (<see cref="ShowMusicSync.PlanJoin"/>),
/// accounting for the learned output latency so the first beat is heard together with the lights.</para>
/// <para><b>Seeking.</b> A single seek while playing moves the music with a click-free seek. Rapid seeks (closer than
/// 200 ms), scrubbing and held fast-forward/rewind silence the music; it rejoins in sync 150 ms after the last seek or
/// when the scrub ends. A scrub that is never ended expires after 3 s without seeks.</para>
/// <para>Music never touches DMX output (safety): it only follows the transport. Release All, project open and app exit
/// stop it.</para>
/// </remarks>
public sealed class ShowMusicService : IShowMusicService, IDisposable
{
    private const double LookAheadSeconds = 0.04;
    private const double RapidSeekGapSeconds = 0.2;
    private const double SettleSeconds = 0.15;
    private const double HoldFailsafeSeconds = 3;
    private const double WarmUpTimeoutSeconds = 1;
    private const double HeardWindowSeconds = 0.5;
    private const double StallSeconds = 0.3;
    private const double CorrectionCooldownSeconds = 0.5;
    private const double MaxLightCorrectionSeconds = 1;
    private const double InitialLatencySeconds = 0.05;
    private const double MaxLatencySeconds = 0.5;
    private const int MaxIdlePlayers = 3;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// The engine applies a seek at its next tick and then advances by that whole tick, so the lights land on average
    /// half a tick ahead of the requested position; aiming half a tick earlier cancels that out.
    /// </summary>
    private static readonly double HalfEngineTickSeconds = 0.5 / EngineHost.DefaultTickRateHz;

    private readonly ProjectService _projects;
    private readonly IConsolePlayback _playback;
    private readonly IAudioAnalyzer _analyzer;
    private readonly Func<IAudioPlayer> _playerFactory;
    private readonly ILogger<ShowMusicService> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<Guid, ShowMusicSession> _sessions = [];
    private readonly List<IdlePlayer> _idle = [];
    private readonly List<Guid> _scratch = [];
    private readonly object _statusGate = new();
    private readonly Dictionary<Guid, StatusEntry> _status = [];
    private readonly object _analysisGate = new();
    private readonly Dictionary<string, AudioAnalysis> _analyses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Output latency (Play → first sample heard), learned from every join; shared by all players.</summary>
    private double _latencySeconds = InitialLatencySeconds;
    private bool _tickErrorLogged;
    private bool _disposed;

    public ShowMusicService(
        ProjectService projects,
        IConsolePlayback playback,
        IAudioAnalyzer analyzer,
        Func<IAudioPlayer> playerFactory,
        ILogger<ShowMusicService> logger)
    {
        _projects = projects;
        _playback = playback;
        _analyzer = analyzer;
        _playerFactory = playerFactory;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher) { Interval = SyncInterval };
        _timer.Tick += OnTick;

        _playback.ShowTransport += OnShowTransport;
        _projects.ProjectChanged += OnProjectChanged;
        _projects.StateChanged += OnProjectStateChanged;
        _projects.ContentChanged += OnContentChanged;
        _dispatcher.ShutdownStarted += OnDispatcherShutdown;
    }

    public event EventHandler<Guid>? StateChanged;

    private double Now => _clock.Elapsed.TotalSeconds;

    // ───────────────────────────── Status ─────────────────────────────

    public ShowMusicState GetState(Guid showId) => GetStatus(showId).State;

    public string? GetStatusMessage(Guid showId) => GetStatus(showId).Message;

    public string? ResolvePath(Show show)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (show.Audio is not { } audio || !HasFile(audio))
        {
            return null;
        }

        try
        {
            return ShowAudioPaths.Resolve(audio, _projects.FilePath, File.Exists);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolving the music of show {ShowId} failed", show.Id);
            return null;
        }
    }

    public async Task<AudioAnalysis?> GetAnalysisAsync(Show show, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (show.Audio is not { } audio || !HasFile(audio))
        {
            return null;
        }

        var path = ResolvePath(show);
        if (path is null)
        {
            SetStatus(show, ShowMusicState.Missing, MissingMessage(audio));
            return null;
        }

        var key = AnalysisKey(path);
        lock (_analysisGate)
        {
            if (_analyses.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        // A playing session keeps its own state; only an idle show shows "Loading" while it is analysed.
        var showsLoading = !_sessions.ContainsKey(show.Id) && GetState(show.Id) != ShowMusicState.Error;
        if (showsLoading)
        {
            SetStatus(show, ShowMusicState.Loading, null);
        }

        try
        {
            var analysis = await _analyzer.AnalyzeAsync(path, progress, cancellationToken);
            lock (_analysisGate)
            {
                _analyses[key] = analysis;
            }

            if (showsLoading)
            {
                SetStatus(show, ShowMusicState.Ready, null);
            }

            return analysis;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (showsLoading)
            {
                SetStatus(show, ShowMusicState.Ready, null);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analysing music {Path} failed", path);
            SetStatus(show, ShowMusicState.Error, CannotPlayMessage(path, ex));
            return null;
        }
    }

    public void OnShowAudioChanged(Guid showId) => OnUiThread(() => ApplyShowAudioChange(showId));

    public void Preload(Guid showId) => OnUiThread(() => PreloadNow(showId));

    /// <summary>
    /// Stops all music at once and releases every player (app exit, safety rule 6: call it in step 1 of the close
    /// sequence, together with the engine Release All). Call on the UI thread.
    /// </summary>
    public void StopAll()
    {
        EndAllSessions(keepPlayers: false);
        ClearIdle();
        UpdateTimer();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _playback.ShowTransport -= OnShowTransport;
        _projects.ProjectChanged -= OnProjectChanged;
        _projects.StateChanged -= OnProjectStateChanged;
        _projects.ContentChanged -= OnContentChanged;
        _dispatcher.ShutdownStarted -= OnDispatcherShutdown;
        try
        {
            if (_dispatcher.CheckAccess() || _dispatcher.HasShutdownStarted)
            {
                StopAll();
            }
            else
            {
                _dispatcher.Invoke(StopAll);
            }

            _timer.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stopping show music failed");
        }

        _disposed = true;
    }

    // ───────────────────────────── Transport ─────────────────────────────

    private void OnShowTransport(object? sender, ShowTransportEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            HandleTransport(e);
        }
        else if (!_dispatcher.HasShutdownStarted)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => HandleTransport(e)));
        }
    }

    private void HandleTransport(ShowTransportEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var now = Now;
            _sessions.TryGetValue(e.ShowId, out var session);
            switch (e.Kind)
            {
                case ShowTransportKind.Started:
                    OnStarted(e.ShowId, e.PositionSeconds, now);
                    break;

                case ShowTransportKind.Paused when session is not null:
                    session.EnginePlaying = false;
                    session.NeedsJoin = false;
                    Silence(session);
                    session.Clock.Reset(e.PositionSeconds, now, running: false);
                    break;

                case ShowTransportKind.Resumed when session is not null:
                    session.EnginePlaying = true;
                    session.Clock.Reset(e.PositionSeconds, now, running: true);
                    Rejoin(session, now, isSeek: false);
                    break;

                case ShowTransportKind.Seeked when session is not null:
                    Reposition(session, e.PositionSeconds, now);
                    break;

                case ShowTransportKind.Stopped:
                case ShowTransportKind.Ended:
                    // Posted from the poll thread: ignore it when the show was started again in the meantime.
                    if (!_playback.IsShowRunning(e.ShowId))
                    {
                        EndSession(e.ShowId, keepPlayer: true);
                    }

                    break;

                case ShowTransportKind.AllStopped:
                    EndAllSessions(keepPlayers: true);
                    break;

                case ShowTransportKind.ScrubStarted when session is not null:
                    session.HoldCount++;
                    session.HoldExpires = now + HoldFailsafeSeconds;
                    session.NeedsJoin = session.EnginePlaying;
                    Silence(session);
                    break;

                case ShowTransportKind.ScrubEnded when session is { HoldCount: > 0 }:
                    session.HoldCount--;
                    if (session.HoldCount == 0)
                    {
                        session.SettleUntil = 0;
                        session.NeedsJoin = session.EnginePlaying;
                        TryJoin(session, now);
                    }

                    break;
            }

            UpdateTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show music could not follow {Kind} of show {ShowId}", e.Kind, e.ShowId);
        }
    }

    private void OnStarted(Guid showId, double position, double now)
    {
        if (!_playback.IsShowRunning(showId))
        {
            return;
        }

        var playing = !_playback.IsShowPaused(showId);
        if (_sessions.TryGetValue(showId, out var session))
        {
            // Stopped and started again before the stop was handled: reuse the session.
            session.EnginePlaying = playing;
            Reposition(session, position, now);
            return;
        }

        if (FindShow(showId) is { } show)
        {
            StartSession(show, position, playing, now);
        }
    }

    /// <summary>The user moved the show (seek, restart): re-anchor the light clock and bring the music there.</summary>
    private void Reposition(ShowMusicSession session, double position, double now)
    {
        session.Clock.Reset(position, now, session.EnginePlaying);
        Rejoin(session, now, isSeek: true);
    }

    /// <summary>The music must play from a new place (seek, resume, offset change).</summary>
    private void Rejoin(ShowMusicSession session, double now, bool isSeek)
    {
        var rapid = isSeek && now - session.LastSeekTime < RapidSeekGapSeconds;
        if (isSeek)
        {
            session.LastSeekTime = now;
        }

        session.Drift.Reset();
        session.WarmingUp = false;
        session.HoldLightsUntilHeard = false;
        if (!session.EnginePlaying)
        {
            Silence(session);
            session.NeedsJoin = false;
            return;
        }

        session.NeedsJoin = true;
        if (session.HoldCount > 0)
        {
            session.HoldExpires = now + HoldFailsafeSeconds;
            Silence(session);
            return;
        }

        if (rapid)
        {
            // Rapid seeking (arrow key repeat, quick clicks): no stutter of short snippets; rejoin once it settles.
            Silence(session);
            session.SettleUntil = now + SettleSeconds;
            return;
        }

        TryJoin(session, now);
    }

    // ───────────────────────────── Sessions ─────────────────────────────

    private void StartSession(Show show, double position, bool playing, double now)
    {
        if (show.Audio is not { } audio || !HasFile(audio) || audio.Muted)
        {
            // No music, or muted from the start: the show runs on the engine clock alone.
            return;
        }

        var path = ResolvePath(show);
        if (path is null)
        {
            // Safety rule 10: visible in the UI, not only logged. The show plays without music.
            _logger.LogWarning("Music of show {ShowId} not found: {File}", show.Id, audio.FilePath);
            SetStatus(show, ShowMusicState.Missing, MissingMessage(audio));
            return;
        }

        var session = new ShowMusicSession(show)
        {
            Path = path,
            OffsetSeconds = SafeOffset(audio.OffsetSeconds),
            EnginePlaying = playing,
            NeedsJoin = playing,
        };
        session.Clock.Reset(position, now, playing);
        _sessions[show.Id] = session;

        var (player, loadTask) = TakeIdle(path) ?? CreatePlayer(path);
        _ = LoadSessionAsync(session, player, loadTask);
    }

    private async Task LoadSessionAsync(ShowMusicSession session, IAudioPlayer player, Task loadTask)
    {
        var cancellation = new CancellationTokenSource();
        session.LoadCancellation = cancellation;
        session.Player = player;
        session.LoadTask = loadTask;
        session.Loaded = false;
        if (!loadTask.IsCompletedSuccessfully)
        {
            SetStatus(session.Show, ShowMusicState.Loading, null);
        }

        try
        {
            // Continues on the UI thread (started there).
            await loadTask.WaitAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The session ended or reloaded; it took care of the player.
            return;
        }
        catch (Exception ex)
        {
            if (session.LoadCancellation == cancellation)
            {
                Fail(session, CannotPlayMessage(session.Path, ex), ex);
            }

            return;
        }

        if (session.LoadCancellation != cancellation || _disposed)
        {
            return;
        }

        session.Loaded = true;
        session.SongDurationSeconds = player.Duration.TotalSeconds;
        ApplyLevels(session);
        SetStatus(session.Show, ShowMusicState.Ready, null);
        TryJoin(session, Now);
        UpdateTimer();
    }

    private void EndSession(Guid showId, bool keepPlayer)
    {
        if (!_sessions.Remove(showId, out var session))
        {
            return;
        }

        session.LoadCancellation?.Cancel();
        session.LoadCancellation = null;
        if (session.Player is { } player)
        {
            Silence(session);
            session.Player = null;
            if (keepPlayer && session.Path is { } path)
            {
                AddIdle(path, player, session.LoadTask ?? Task.CompletedTask);
            }
            else
            {
                DisposePlayer(player, session.LoadTask);
            }
        }

        // Back to the general state; an error stays visible until the music is relinked or the show plays again.
        if (PeekStatus(showId) is { State: ShowMusicState.Loading })
        {
            SetStatus(session.Show, ShowMusicState.Ready, null);
        }
    }

    private void EndAllSessions(bool keepPlayers)
    {
        _scratch.Clear();
        _scratch.AddRange(_sessions.Keys);
        foreach (var id in _scratch)
        {
            EndSession(id, keepPlayers);
        }

        _scratch.Clear();
    }

    /// <summary>Playback failed: the show goes on without music; the error is shown on the show (safety rule 10).</summary>
    private void Fail(ShowMusicSession session, string message, Exception? exception)
    {
        _logger.LogWarning(exception, "Music of show {ShowId} failed: {Message}", session.ShowId, message);
        if (session.Player is { } player)
        {
            session.Player = null;
            DisposePlayer(player, session.LoadTask);
        }

        session.Loaded = false;
        session.AudioPlaying = false;
        session.NeedsJoin = false;
        session.WarmingUp = false;
        session.HoldLightsUntilHeard = false;
        SetStatus(session.Show, ShowMusicState.Error, message);
    }

    private void ApplyShowAudioChange(Guid showId)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var now = Now;
            InvalidateStatus(showId);
            var show = FindShow(showId);
            if (_sessions.TryGetValue(showId, out var session))
            {
                var path = show is null ? null : ResolvePath(show);
                if (show is null || path is null)
                {
                    EndSession(showId, keepPlayer: false);
                }
                else if (session.Player is null || !SamePath(path, session.Path))
                {
                    // Relinked, or a retry after an error: open the new file and rejoin where the show is.
                    var playing = session.EnginePlaying;
                    var position = session.Clock.Estimate(now);
                    EndSession(showId, keepPlayer: false);
                    StartSession(show, position, playing, now);
                }
                else
                {
                    ApplySettings(session, show, now);
                }
            }
            else if (show is not null && _playback.IsShowRunning(showId))
            {
                // Music added or unmuted while the show runs: it joins right away.
                StartSession(show, _playback.GetShowPosition(showId), !_playback.IsShowPaused(showId), now);
            }

            RaiseStateChanged(showId);
            UpdateTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying the music change of show {ShowId} failed", showId);
        }
    }

    /// <summary>Re-applies volume, mute, fade and offset of a running session (cheap; after every project edit).</summary>
    private void ApplySettings(ShowMusicSession session, Show show, double now)
    {
        session.Show = show;
        if (show.Audio is not { } audio)
        {
            EndSession(session.ShowId, keepPlayer: false);
            return;
        }

        var offset = SafeOffset(audio.OffsetSeconds);
        if (Math.Abs(offset - session.OffsetSeconds) > 1e-6)
        {
            session.OffsetSeconds = offset;
            Rejoin(session, now, isSeek: true);
        }

        ApplyLevels(session);
    }

    /// <summary>Volume (0 while muted, so a live mute keeps the music in sync) and the fade-out before the show end.</summary>
    private void ApplyLevels(ShowMusicSession session)
    {
        if (session.Player is not { } player || !session.Loaded || session.Show.Audio is not { } audio)
        {
            return;
        }

        player.Volume = audio.Muted ? 0f : (float)ShowAudioTiming.ClampVolume(audio.Volume);

        // The fade ends at the show end; IAudioPlayer fades in song time.
        var fade = audio.FadeOutSeconds;
        var length = session.Show.LengthSeconds;
        if (double.IsFinite(fade) && fade > 0 && double.IsFinite(length))
        {
            var start = ShowAudioTiming.ToSongTime(length - fade, session.OffsetSeconds);
            var duration = fade;
            if (start < 0)
            {
                // The fade begins before the song does: fade over the part that is played.
                duration += start;
                start = 0;
            }

            if (duration > 0)
            {
                player.SetFadeOut(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(duration));
                return;
            }
        }

        player.SetFadeOut(TimeSpan.Zero, TimeSpan.Zero);
    }

    // ───────────────────────────── Sync ─────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            var now = Now;
            _scratch.Clear();
            foreach (var session in _sessions.Values)
            {
                if (!_playback.IsShowRunning(session.ShowId))
                {
                    _scratch.Add(session.ShowId);
                    continue;
                }

                // Self-healing: a missed transport event is noticed here within one tick.
                var paused = _playback.IsShowPaused(session.ShowId);
                if (paused == session.EnginePlaying)
                {
                    session.EnginePlaying = !paused;
                    session.Clock.Reset(_playback.GetShowPosition(session.ShowId), now, !paused);
                    Rejoin(session, now, isSeek: false);
                }

                if (session.EnginePlaying)
                {
                    Sync(session, now);
                }
            }

            foreach (var id in _scratch)
            {
                EndSession(id, keepPlayer: true);
            }

            _scratch.Clear();
            UpdateTimer();
        }
        catch (Exception ex)
        {
            if (!_tickErrorLogged)
            {
                _tickErrorLogged = true;
                _logger.LogError(ex, "Show music sync failed");
            }
        }
    }

    private void Sync(ShowMusicSession session, double now)
    {
        if (_playback.TryGetShowClock(session.ShowId, out var enginePosition, out var sequence))
        {
            session.Clock.Observe(enginePosition, sequence, now);
        }

        if (session.HoldCount > 0 && now >= session.HoldExpires)
        {
            _logger.LogWarning("Scrub of show {ShowId} was not ended; music rejoins", session.ShowId);
            session.HoldCount = 0;
            session.NeedsJoin = true;
        }

        if (session.Player is not { } player || !session.Loaded)
        {
            return;
        }

        if (session.Clock.Estimate(now) >= session.Show.LengthSeconds)
        {
            // The lights reach the show end: no music past it (the poll ends the session a moment later).
            Silence(session);
            return;
        }

        if (session.NeedsJoin)
        {
            TryJoin(session, now);
            return;
        }

        if (!session.AudioPlaying)
        {
            return;
        }

        var audioPosition = player.Position.TotalSeconds;
        if (session.WarmingUp && !FinishWarmUp(session, audioPosition, now))
        {
            return;
        }

        if (audioPosition > session.LastAudioPosition + 0.0005)
        {
            session.LastAudioPosition = audioPosition;
            session.LastAudioAdvanceTime = now;
        }
        else if (now - session.LastAudioAdvanceTime > StallSeconds)
        {
            // The music is not moving (device stall, song end): the lights keep their own clock.
            session.Drift.Reset();
            return;
        }

        if (audioPosition >= session.SongDurationSeconds - ShowMusicSync.EndMarginSeconds || !session.Clock.HasSample)
        {
            return;
        }

        var musicShowTime = ShowAudioTiming.ToShowTime(audioPosition, session.OffsetSeconds);
        session.Drift.Add(musicShowTime - session.Clock.Estimate(now));
        if (now < session.NextCorrectionTime || !session.Drift.TryGetCorrection(out var drift))
        {
            return;
        }

        session.Drift.Reset();
        session.NextCorrectionTime = now + CorrectionCooldownSeconds;
        if (Math.Abs(drift) <= MaxLightCorrectionSeconds)
        {
            // Lights follow the music.
            SyncLights(session, musicShowTime, now);
        }
        else
        {
            // A jump this large means the music skipped (device glitch): bring the music back to the lights instead.
            _logger.LogWarning("Music of show {ShowId} was {Drift:0.000} s off; rejoining", session.ShowId, drift);
            session.NeedsJoin = true;
            TryJoin(session, now);
        }
    }

    /// <summary>
    /// After Play/Seek the player's position stands still until the first sample is heard. Returns true once it is
    /// heard (and learns the output latency from it); while waiting it holds the lights at the song start if asked.
    /// </summary>
    private bool FinishWarmUp(ShowMusicSession session, double audioPosition, double now)
    {
        var advanced = audioPosition - session.JoinAudioPosition;
        if (advanced > 0.001 && advanced < HeardWindowSeconds)
        {
            if (session.MeasureLatency && advanced < 0.1)
            {
                var measured = now - advanced - session.JoinTime;
                if (measured >= 0 && measured <= MaxLatencySeconds)
                {
                    _latencySeconds = (0.7 * _latencySeconds) + (0.3 * measured);
                }
            }

            session.WarmingUp = false;
            session.LastAudioPosition = audioPosition;
            session.LastAudioAdvanceTime = now;
            if (session.HoldLightsUntilHeard)
            {
                session.HoldLightsUntilHeard = false;
                SyncLights(session, ShowAudioTiming.ToShowTime(audioPosition, session.OffsetSeconds), now);
            }

            return true;
        }

        if (now - session.JoinTime > WarmUpTimeoutSeconds)
        {
            // Never heard (device stalled): stop holding the lights; stall detection keeps them on their own clock.
            session.WarmingUp = false;
            session.HoldLightsUntilHeard = false;
            return true;
        }

        if (session.HoldLightsUntilHeard && session.Clock.Estimate(now) > session.HoldLightsAt + 0.02)
        {
            SyncLights(session, session.HoldLightsAt, now);
        }

        return false;
    }

    /// <summary>Starts the music where the lights are (or schedules it), if nothing holds it back.</summary>
    private void TryJoin(ShowMusicSession session, double now)
    {
        if (!session.NeedsJoin || !session.EnginePlaying || !session.Loaded || session.Player is not { } player
            || session.HoldCount > 0 || now < session.SettleUntil)
        {
            return;
        }

        var showTime = session.Clock.Estimate(now);
        if (showTime >= session.Show.LengthSeconds)
        {
            Silence(session);
            return;
        }

        var plan = ShowMusicSync.PlanJoin(showTime, session.OffsetSeconds, session.SongDurationSeconds, _latencySeconds, LookAheadSeconds);
        switch (plan.Kind)
        {
            case ShowMusicJoinKind.Wait:
            case ShowMusicJoinKind.SongOver:
                // Before the song (it is started on time by a later tick) or after it: silent, engine clock is master.
                Silence(session);
                return;

            case ShowMusicJoinKind.StartAtSongStart:
                {
                    var fromSilence = !session.AudioPlaying;
                    player.Seek(TimeSpan.Zero);
                    Play(session, player);
                    BeginWarmUp(session, 0, now, fromSilence);
                    if (plan.EngineShowTimeSeconds >= 0)
                    {
                        SyncLights(session, plan.EngineShowTimeSeconds, now);
                    }
                    else
                    {
                        // The song starts at the very show start: hold the lights at 0 until its first sample is heard.
                        session.HoldLightsUntilHeard = true;
                        session.HoldLightsAt = 0;
                        SyncLights(session, 0, now);
                    }

                    break;
                }

            case ShowMusicJoinKind.JoinMidSong:
                {
                    var fromSilence = !session.AudioPlaying;
                    player.Seek(TimeSpan.FromSeconds(plan.AudioPositionSeconds));
                    Play(session, player);
                    BeginWarmUp(session, plan.AudioPositionSeconds, now, fromSilence);
                    break;
                }
        }

        session.NeedsJoin = false;
    }

    private void Play(ShowMusicSession session, IAudioPlayer player)
    {
        ApplyLevels(session);
        if (!session.AudioPlaying)
        {
            player.Play();
            session.AudioPlaying = true;
        }
    }

    private static void BeginWarmUp(ShowMusicSession session, double audioPosition, double now, bool measureLatency)
    {
        session.WarmingUp = true;
        session.MeasureLatency = measureLatency;
        session.JoinTime = now;
        session.JoinAudioPosition = audioPosition;
        session.LastAudioPosition = audioPosition;
        session.LastAudioAdvanceTime = now;
        session.NextCorrectionTime = now;
        session.Drift.Reset();
    }

    /// <summary>Moves the lights to <paramref name="showTime"/> (drift correction; no transport event).</summary>
    private void SyncLights(ShowMusicSession session, double showTime, double now)
    {
        _playback.SyncShowPosition(session.ShowId, Math.Max(0, showTime - HalfEngineTickSeconds));
        session.Clock.Reset(Math.Max(0, showTime), now, running: true);
    }

    private static void Silence(ShowMusicSession session)
    {
        if (session.AudioPlaying && session.Player is { } player)
        {
            player.Pause();
        }

        session.AudioPlaying = false;
        session.WarmingUp = false;
        session.HoldLightsUntilHeard = false;
        session.Drift.Reset();
    }

    private void UpdateTimer()
    {
        var run = false;
        foreach (var session in _sessions.Values)
        {
            if (session.EnginePlaying)
            {
                run = true;
                break;
            }
        }

        // Start() re-arms the interval, so only call it when stopped (frequent events must not postpone ticks).
        if (run && !_disposed)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else if (_timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    // ───────────────────────────── Players ─────────────────────────────

    private (IAudioPlayer Player, Task LoadTask) CreatePlayer(string path)
    {
        var player = _playerFactory();
        player.Error += OnPlayerError;
        Task loadTask;
        try
        {
            loadTask = player.LoadAsync(path);
        }
        catch (Exception ex)
        {
            loadTask = Task.FromException(ex);
        }

        return (player, loadTask);
    }

    private void DisposePlayer(IAudioPlayer player, Task? loadTask)
    {
        player.Error -= OnPlayerError;
        if (loadTask is { IsCompleted: false })
        {
            // Never dispose a player in the middle of opening its file.
            _ = loadTask.ContinueWith(_ => DisposeQuietly(player), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }

        DisposeQuietly(player);
    }

    private void DisposeQuietly(IAudioPlayer player)
    {
        try
        {
            player.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Closing a music player failed");
        }
    }

    private void OnPlayerError(object? sender, AudioErrorEventArgs e)
    {
        if (_disposed || sender is not IAudioPlayer player)
        {
            return;
        }

        OnUiThread(() =>
        {
            foreach (var session in _sessions.Values)
            {
                if (ReferenceEquals(session.Player, player))
                {
                    Fail(session, $"Music stopped: {e.Message}. The show keeps running without music.", e.Exception);
                    return;
                }
            }

            for (var i = 0; i < _idle.Count; i++)
            {
                if (ReferenceEquals(_idle[i].Player, player))
                {
                    _idle.RemoveAt(i);
                    DisposePlayer(player, null);
                    return;
                }
            }
        });
    }

    private void PreloadNow(Guid showId)
    {
        if (_disposed || _sessions.ContainsKey(showId) || FindShow(showId) is not { } show
            || show.Audio is not { Muted: false } audio || !HasFile(audio))
        {
            return;
        }

        var path = ResolvePath(show);
        if (path is null)
        {
            SetStatus(show, ShowMusicState.Missing, MissingMessage(audio));
            return;
        }

        foreach (var idle in _idle)
        {
            if (SamePath(idle.Path, path))
            {
                return;
            }
        }

        var (player, loadTask) = CreatePlayer(path);
        AddIdle(path, player, loadTask);
        _ = ObservePreloadAsync(show, path, player, loadTask);
    }

    private async Task ObservePreloadAsync(Show show, string path, IAudioPlayer player, Task loadTask)
    {
        try
        {
            await loadTask;
        }
        catch (Exception ex)
        {
            var index = _idle.FindIndex(i => ReferenceEquals(i.Player, player));
            if (index >= 0)
            {
                _idle.RemoveAt(index);
                DisposePlayer(player, null);
            }

            // A session that took this player reports the failure itself.
            if (!_sessions.ContainsKey(show.Id))
            {
                _logger.LogWarning(ex, "Preparing music {Path} failed", path);
                SetStatus(show, ShowMusicState.Error, CannotPlayMessage(path, ex));
            }
        }
    }

    private (IAudioPlayer Player, Task LoadTask)? TakeIdle(string path)
    {
        for (var i = 0; i < _idle.Count; i++)
        {
            var idle = _idle[i];
            if (SamePath(idle.Path, path) && !idle.LoadTask.IsFaulted && !idle.LoadTask.IsCanceled)
            {
                _idle.RemoveAt(i);
                return (idle.Player, idle.LoadTask);
            }
        }

        return null;
    }

    /// <summary>Keeps a stopped show's player open (most recent first) so playing it again starts the music at once.</summary>
    private void AddIdle(string path, IAudioPlayer player, Task loadTask)
    {
        _idle.Insert(0, new IdlePlayer(path, player, loadTask));
        while (_idle.Count > MaxIdlePlayers)
        {
            var oldest = _idle[^1];
            _idle.RemoveAt(_idle.Count - 1);
            DisposePlayer(oldest.Player, oldest.LoadTask);
        }
    }

    private void ClearIdle()
    {
        foreach (var idle in _idle)
        {
            DisposePlayer(idle.Player, idle.LoadTask);
        }

        _idle.Clear();
    }

    // ───────────────────────────── Project events ─────────────────────────────

    private void OnProjectChanged(object? sender, EventArgs e) => OnUiThread(() =>
    {
        // Another project: other songs. EngineConsolePlayback also reports AllStopped.
        EndAllSessions(keepPlayers: false);
        ClearIdle();
        lock (_statusGate)
        {
            _status.Clear();
        }

        UpdateTimer();
    });

    /// <summary>The project file moved (Save As): relative music paths resolve differently now.</summary>
    private void OnProjectStateChanged(object? sender, EventArgs e)
    {
        lock (_statusGate)
        {
            _status.Clear();
        }
    }

    private void OnContentChanged(object? sender, ProjectContentChangedEventArgs e)
    {
        if (_sessions.Count == 0 || !_dispatcher.CheckAccess())
        {
            return;
        }

        var now = Now;
        _scratch.Clear();
        _scratch.AddRange(_sessions.Keys);
        foreach (var id in _scratch)
        {
            if (_sessions.TryGetValue(id, out var session) && FindShow(id) is { } show)
            {
                ApplySettings(session, show, now);
            }
        }

        _scratch.Clear();
    }

    private void OnDispatcherShutdown(object? sender, EventArgs e)
    {
        try
        {
            StopAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stopping show music on shutdown failed");
        }
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private StatusEntry GetStatus(Guid showId)
    {
        var show = FindShow(showId);
        lock (_statusGate)
        {
            if (_status.TryGetValue(showId, out var entry) && entry.Matches(show?.Audio))
            {
                return entry;
            }
        }

        var evaluated = Evaluate(show);
        lock (_statusGate)
        {
            _status[showId] = evaluated;
        }

        return evaluated;
    }

    private StatusEntry? PeekStatus(Guid showId)
    {
        lock (_statusGate)
        {
            return _status.TryGetValue(showId, out var entry) ? entry : null;
        }
    }

    private StatusEntry Evaluate(Show? show)
    {
        if (show?.Audio is not { } audio || !HasFile(audio))
        {
            return new StatusEntry(ShowMusicState.None, null, null, null);
        }

        return ResolvePath(show) is null
            ? new StatusEntry(ShowMusicState.Missing, MissingMessage(audio), audio.FilePath, audio.RelativePath)
            : new StatusEntry(ShowMusicState.Ready, null, audio.FilePath, audio.RelativePath);
    }

    private void SetStatus(Show show, ShowMusicState state, string? message)
    {
        var entry = new StatusEntry(state, message, show.Audio?.FilePath, show.Audio?.RelativePath);
        lock (_statusGate)
        {
            if (_status.TryGetValue(show.Id, out var current) && current == entry)
            {
                return;
            }

            _status[show.Id] = entry;
        }

        RaiseStateChanged(show.Id);
    }

    private void InvalidateStatus(Guid showId)
    {
        lock (_statusGate)
        {
            _status.Remove(showId);
        }
    }

    private void RaiseStateChanged(Guid showId) => OnUiThread(() => StateChanged?.Invoke(this, showId));

    private void OnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else if (!_dispatcher.HasShutdownStarted)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }
    }

    private Show? FindShow(Guid showId)
    {
        foreach (var show in _projects.Current.Shows)
        {
            if (show.Id == showId)
            {
                return show;
            }
        }

        return null;
    }

    private static bool HasFile(ShowAudio audio) =>
        !string.IsNullOrWhiteSpace(audio.FilePath) || !string.IsNullOrWhiteSpace(audio.RelativePath);

    private static double SafeOffset(double offset) => double.IsFinite(offset) ? offset : 0;

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string FileName(ShowAudio audio)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(audio.FilePath) ? audio.RelativePath : audio.FilePath);
        return string.IsNullOrEmpty(name) ? "(unknown file)" : name;
    }

    private static string MissingMessage(ShowAudio audio) => $"Music not found: {FileName(audio)} — relink it on the Shows page";

    private static string CannotPlayMessage(string? path, Exception ex) =>
        $"Music can't be played: {Path.GetFileName(path)} — {ex.Message}";

    /// <summary>Path + size + last write: a changed file is analysed again.</summary>
    private static string AnalysisKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private sealed record IdlePlayer(string Path, IAudioPlayer Player, Task LoadTask);

    private sealed record StatusEntry(ShowMusicState State, string? Message, string? FilePath, string? RelativePath)
    {
        /// <summary>Still about the show's current music link (a relink or removal makes it stale).</summary>
        public bool Matches(ShowAudio? audio) => audio is null
            ? FilePath is null && RelativePath is null
            : string.Equals(FilePath, audio.FilePath, StringComparison.Ordinal)
                && string.Equals(RelativePath, audio.RelativePath, StringComparison.Ordinal);
    }
}
