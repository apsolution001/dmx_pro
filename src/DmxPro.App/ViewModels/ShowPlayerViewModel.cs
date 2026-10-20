using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Show player of the Virtual Console: the "Shows" list (every show of the project) and the transport bar at the
/// bottom of the console. The bar follows the show that is running (also one started by a Show widget); when none
/// runs it shows the last played or the selected show. Playback goes through <see cref="IConsolePlayback"/> only.
/// The position is polled ~30 times a second, and only while a show is actually playing. The show's music follows the
/// transport by itself (<see cref="IShowMusicService"/>); this ViewModel only marks scrub / fast-seek gestures
/// (<see cref="IConsolePlayback.BeginShowScrub"/>) so the music stays silent during them, and shows the music state.
/// </summary>
public sealed partial class ShowPlayerViewModel : ObservableObject
{
    /// <summary>Poll interval of the playing position (~30 Hz).</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(33);

    /// <summary>A held fast-forward/rewind repeats every 150 ms; a gap longer than this starts a new press.</summary>
    private static readonly TimeSpan FastSeekRepeatGap = TimeSpan.FromMilliseconds(500);

    /// <summary>Fail-safe end of a fast-seek hold when no release was seen (keyboard activation, focus lost).</summary>
    private static readonly TimeSpan FastSeekReleaseDelay = TimeSpan.FromMilliseconds(400);

    private readonly ProjectService _projects;
    private readonly IConsolePlayback _playback;
    private readonly IShowMusicService? _music;
    private Guid? _activeId;
    private double _cueSeconds;
    private bool _isScrubbing;
    private CancellationTokenSource? _ticker;

    // Music silence holds (IConsolePlayback.BeginShowScrub): one for timeline scrubbing, one for held FF/REW/arrow keys.
    private Guid? _scrubHoldId;
    private Guid? _fastSeekHoldId;
    private int _fastSeekRepeat;
    private long _lastFastSeekTicks;
    private CancellationTokenSource? _fastSeekRelease;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaySelectedCommand), nameof(StopSelectedCommand))]
    private ShowListItemViewModel? _selectedShow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveShow), nameof(HasNoActiveShow), nameof(Progress), nameof(EmptyBarText))]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand), nameof(SkipCommand), nameof(FastSeekCommand))]
    private ShowListItemViewModel? _activeShow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText), nameof(Progress))]
    private double _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(StatusText), nameof(StatusKind), nameof(StatusIcon), nameof(StatusToolTip), nameof(PlayPauseIcon), nameof(PlayPauseToolTip))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(StatusText), nameof(StatusKind), nameof(StatusIcon), nameof(StatusToolTip), nameof(PlayPauseIcon), nameof(PlayPauseToolTip))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusKind), nameof(StatusIcon), nameof(StatusToolTip))]
    private bool _isOverridden;

    /// <summary>Seconds the big skip buttons and the Left/Right keys jump (Settings ▸ Show playback ▸ seek step).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LargeStepBackText), nameof(LargeStepForwardText), nameof(LargeStepBackArgument), nameof(LargeStepForwardArgument))]
    private double _largeStepSeconds = 5;

    /// <param name="projects">The open project.</param>
    /// <param name="playback">Show transport.</param>
    /// <param name="music">Music state for the bar's music mark (optional: without it the bar shows no music state;
    /// the music still follows the transport).</param>
    public ShowPlayerViewModel(ProjectService projects, IConsolePlayback playback, IShowMusicService? music = null)
    {
        _projects = projects;
        _playback = playback;
        _music = music;
        _projects.ContentChanged += (_, _) => RebuildShows();
        if (_music is not null)
        {
            // Raised on the UI thread.
            _music.StateChanged += (_, showId) =>
            {
                foreach (var item in Shows)
                {
                    if (item.Id == showId)
                    {
                        item.SetMusic(_music.GetState(showId), _music.GetStatusMessage(showId));
                    }
                }

                if (showId == ActiveShow?.Id)
                {
                    UpdateMusic();
                }
            };
        }

        RebuildShows();
    }

    /// <summary>Every show of the project, in project order.</summary>
    public ObservableCollection<ShowListItemViewModel> Shows { get; } = [];

    public bool HasShows => Shows.Count > 0;

    public string LargeStepBackText => string.Create(CultureInfo.InvariantCulture, $"−{LargeStepSeconds:0} s");

    public string LargeStepForwardText => string.Create(CultureInfo.InvariantCulture, $"+{LargeStepSeconds:0} s");

    public string LargeStepBackArgument => (-LargeStepSeconds).ToString(CultureInfo.InvariantCulture);

    public string LargeStepForwardArgument => LargeStepSeconds.ToString(CultureInfo.InvariantCulture);

    public bool HasNoShows => Shows.Count == 0;

    public bool HasActiveShow => ActiveShow is not null;

    public bool HasNoActiveShow => ActiveShow is null;

    /// <summary>What the bar says when it has no show to show.</summary>
    public string EmptyBarText => HasShows
        ? "No show selected — pick one on the Shows tab."
        : "No shows yet — build one on the Shows page.";

    public bool IsPlaying => IsRunning && !IsPaused;

    /// <summary>Playhead position 0..1 of the show length.</summary>
    public double Progress => ActiveShow is { LengthSeconds: > 0 } show ? Math.Clamp(Position / show.LengthSeconds, 0, 1) : 0;

    public string PositionText => FormatPosition(Position);

    public string StatusText =>
        !IsRunning ? "STOPPED"
        : IsPaused ? "PAUSED"
        : IsOverridden ? "OVERRIDE — show muted"
        : "PLAYING";

    /// <summary>Tag for the shared StatusPill style: "" neutral, "Connected" (paused), "Sending" (playing), "Warning".</summary>
    public string StatusKind =>
        !IsRunning ? string.Empty
        : IsPaused ? "Connected"
        : IsOverridden ? "Warning"
        : "Sending";

    public string StatusIcon =>
        !IsRunning ? "Stop"
        : IsPaused ? "Pause"
        : IsOverridden ? "Warning"
        : "Play";

    public string StatusToolTip =>
        !IsRunning ? "The show is stopped."
        : IsPaused ? "The show is paused: the timeline is frozen and its lights hold their look."
        : IsOverridden ? "A console function is on, so the show's lights are muted. The timeline keeps running; switch the function off to bring the show back."
        : "The show is playing.";

    /// <summary>Music state of the show in the bar (None when it has no music or no music service is attached).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMusic), nameof(HasMusicProblem), nameof(MusicIcon), nameof(MusicToolTip), nameof(MusicProblemText))]
    private ShowMusicState _musicState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MusicToolTip))]
    private string? _musicMessage;

    public bool HasMusic => MusicState != ShowMusicState.None;

    /// <summary>The song is missing or cannot be played: the show runs without music (safety rule 10: shown, not only logged).</summary>
    public bool HasMusicProblem => MusicState is ShowMusicState.Missing or ShowMusicState.Error;

    public string MusicIcon => HasMusicProblem ? "Warning" : "Music";

    /// <summary>Short text next to the warning icon.</summary>
    public string MusicProblemText => MusicState == ShowMusicState.Missing ? "Music missing" : "Music error";

    public string MusicToolTip => MusicState switch
    {
        ShowMusicState.Loading => "Loading the show's music…",
        ShowMusicState.Ready => "This show plays its music in sync with the lights.",
        ShowMusicState.Missing or ShowMusicState.Error => MusicMessage ?? "The show's music cannot be played.",
        _ => string.Empty,
    };

    public string PlayPauseIcon => IsPlaying ? "Pause" : "Play";

    public string PlayPauseToolTip => IsPlaying ? "Pause (Space)" : IsPaused ? "Resume (Space)" : "Play (Space)";

    /// <summary>"m:ss.t", e.g. 0:42.1 (tenths are truncated so the display never runs ahead of the show).</summary>
    public static string FormatPosition(double seconds)
    {
        var tenths = (long)Math.Floor(Math.Max(0, double.IsFinite(seconds) ? seconds : 0) * 10);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{tenths / 600}:{tenths % 600 / 10:00}.{tenths % 10}");
    }

    /// <summary>"m:ss", rounded to whole seconds, e.g. 3:00.</summary>
    public static string FormatLength(double seconds)
    {
        var whole = (long)Math.Round(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }

    /// <summary>Clamps a seek target to 0..length (non-finite values become 0).</summary>
    public static double ClampPosition(double seconds, double lengthSeconds) =>
        double.IsFinite(seconds) ? Math.Clamp(seconds, 0, Math.Max(0, lengthSeconds)) : 0;

    // ───────────────────────────── Transport bar ─────────────────────────────

    /// <summary>Play / pause / resume the show in the bar. Space.</summary>
    [RelayCommand(CanExecute = nameof(HasActiveShow))]
    private void PlayPause()
    {
        if (ActiveShow is not { } show)
        {
            return;
        }

        if (IsPlaying)
        {
            _playback.PauseShow(show.Id);
            Refresh();
        }
        else
        {
            Play(show);
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop()
    {
        if (ActiveShow is { } show)
        {
            _playback.StopShow(show.Id);
            _cueSeconds = 0;
            Refresh();
        }
    }

    /// <summary>Jumps by a number of seconds given as invariant text ("-5", "1").</summary>
    [RelayCommand(CanExecute = nameof(HasActiveShow))]
    private void Skip(string? seconds)
    {
        if (double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
        {
            SeekBy(delta);
        }
    }

    /// <summary>Moves the playhead by <paramref name="deltaSeconds"/> (clamped to the show).</summary>
    public void SeekBy(double deltaSeconds) => SeekTo(CurrentPosition() + deltaSeconds);

    /// <summary>
    /// Moves the playhead. A running (or paused) show seeks in the engine; a stopped show remembers the point so Play
    /// starts there.
    /// </summary>
    public void SeekTo(double seconds)
    {
        if (ActiveShow is not { } show)
        {
            return;
        }

        var target = ClampPosition(seconds, show.LengthSeconds);
        if (IsRunning)
        {
            _playback.SeekShow(show.Id, target);
        }
        else
        {
            _cueSeconds = target;
        }

        Position = target;
    }

    /// <summary>
    /// Fast-forward (<c>"1"</c>) / rewind (<c>"-1"</c>), bound to RepeatButtons: the press jumps one seek step, holding
    /// repeats every 150 ms and accelerates (<see cref="ShowMusicSync.FastSeekStep"/>). The music is silent while held
    /// and rejoins in sync on release (<see cref="EndFastSeek"/>, or a fail-safe 400 ms after the last repeat).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveShow))]
    private void FastSeek(string? direction)
    {
        if (!double.TryParse(direction, NumberStyles.Float, CultureInfo.InvariantCulture, out var sign) || sign == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        _fastSeekRepeat = now - _lastFastSeekTicks < FastSeekRepeatGap.TotalMilliseconds ? _fastSeekRepeat + 1 : 0;
        _lastFastSeekTicks = now;
        HoldSeekBy(Math.Sign(sign) * ShowMusicSync.FastSeekStep(LargeStepSeconds, _fastSeekRepeat));
    }

    /// <summary>
    /// A seek that is part of a held gesture (FF/REW repeat, arrow key auto-repeat): silences the music until
    /// <see cref="EndFastSeek"/> so it does not stutter through snippets; the lights follow every jump.
    /// </summary>
    public void HoldSeekBy(double deltaSeconds)
    {
        if (ActiveShow is { } show && IsRunning && _fastSeekHoldId is null)
        {
            _fastSeekHoldId = show.Id;
            _playback.BeginShowScrub(show.Id);
        }

        SeekBy(deltaSeconds);
        ScheduleFastSeekRelease();
    }

    /// <summary>FF/REW button or arrow key released: the music rejoins at the new position.</summary>
    public void EndFastSeek()
    {
        _fastSeekRelease?.Cancel();
        _fastSeekRelease?.Dispose();
        _fastSeekRelease = null;
        _fastSeekRepeat = 0;
        if (_fastSeekHoldId is { } id)
        {
            _fastSeekHoldId = null;
            _playback.EndShowScrub(id);
        }
    }

    private void ScheduleFastSeekRelease()
    {
        _fastSeekRelease?.Cancel();
        _fastSeekRelease?.Dispose();
        _fastSeekRelease = new CancellationTokenSource();
        _ = ReleaseFastSeekLaterAsync(_fastSeekRelease.Token);
    }

    /// <summary>Started on the UI thread, so it continues there.</summary>
    private async Task ReleaseFastSeekLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(FastSeekReleaseDelay, token);
            EndFastSeek();
        }
        catch (OperationCanceledException)
        {
            // Another repeat came, or the release was seen.
        }
    }

    /// <summary>
    /// Mouse down on the timeline: the poll stops moving the playhead until <see cref="EndScrub"/>, and a running
    /// show's music is silent while dragging (it rejoins at the drop point).
    /// </summary>
    public void BeginScrub()
    {
        _isScrubbing = ActiveShow is not null;
        if (_isScrubbing && IsRunning && ActiveShow is { } show && _scrubHoldId is null)
        {
            _scrubHoldId = show.Id;
            _playback.BeginShowScrub(show.Id);
        }
    }

    /// <summary>Scrub to a point of the timeline (0..1 of its width).</summary>
    public void ScrubTo(double fraction)
    {
        if (ActiveShow is { } show && double.IsFinite(fraction))
        {
            SeekTo(Math.Clamp(fraction, 0, 1) * show.LengthSeconds);
        }
    }

    public void EndScrub()
    {
        if (_scrubHoldId is { } id)
        {
            _scrubHoldId = null;
            _playback.EndShowScrub(id);
        }

        if (_isScrubbing)
        {
            _isScrubbing = false;
            Refresh();
        }
    }

    // ───────────────────────────── Shows list ─────────────────────────────

    /// <summary>List toolbar / Enter / double-click: plays the selected show (resumes it when paused).</summary>
    [RelayCommand(CanExecute = nameof(CanPlaySelected))]
    private void PlaySelected()
    {
        if (SelectedShow is { IsPlaying: false } show)
        {
            Play(show);
        }
    }

    private bool CanPlaySelected() => SelectedShow is not null;

    [RelayCommand(CanExecute = nameof(CanStopSelected))]
    private void StopSelected()
    {
        if (SelectedShow is { IsRunning: true } show)
        {
            _playback.StopShow(show.Id);
            if (show.Id == _activeId)
            {
                _cueSeconds = 0;
            }

            Refresh();
        }
    }

    private bool CanStopSelected() => SelectedShow is { IsRunning: true };

    /// <summary>Row button / Space in the list: pauses the row's show when it plays, otherwise plays it.</summary>
    [RelayCommand]
    private void PlayPauseRow(ShowListItemViewModel? show)
    {
        if (show is null)
        {
            return;
        }

        if (show.IsPlaying)
        {
            _playback.PauseShow(show.Id);
            Refresh();
        }
        else
        {
            Play(show);
        }
    }

    partial void OnSelectedShowChanged(ShowListItemViewModel? value)
    {
        // While nothing runs, the bar follows the list selection so its timeline can be cued before playing.
        if (value is not null && !IsRunning && value.Id != _activeId)
        {
            _activeId = value.Id;
            _cueSeconds = 0;
            Refresh();
        }
    }

    // ───────────────────────────── State ─────────────────────────────

    /// <summary>Re-reads the playback state. Call on the UI thread after <see cref="IConsolePlayback.StateChanged"/>.</summary>
    public void Refresh()
    {
        foreach (var item in Shows)
        {
            item.SetState(_playback.IsShowRunning(item.Id), _playback.IsShowPaused(item.Id), _playback.IsShowOverridden);
            if (_music is not null)
            {
                // Cheap (cached in the service); also picks up relinks made on the Shows page.
                item.SetMusic(_music.GetState(item.Id), _music.GetStatusMessage(item.Id));
            }
        }

        var active = FindActive();
        if (active?.Id != _activeId)
        {
            _cueSeconds = 0;
        }

        var activeChanged = !ReferenceEquals(active, ActiveShow);
        _activeId = active?.Id;
        ActiveShow = active;
        if (activeChanged && active is not null && !active.IsRunning)
        {
            // The bar's show is the likely next one: open its song now so Play starts the music on the first beat.
            _music?.Preload(active.Id);
        }

        UpdateMusic();
        IsRunning = active?.IsRunning == true;
        IsPaused = active?.IsPaused == true;
        IsOverridden = IsPlaying && _playback.IsShowOverridden;
        if (!_isScrubbing)
        {
            Position = CurrentPosition();
        }

        StopSelectedCommand.NotifyCanExecuteChanged();
        PlaySelectedCommand.NotifyCanExecuteChanged();
        UpdateTicker(IsPlaying);
    }

    private void UpdateMusic()
    {
        if (_music is null || ActiveShow is not { } show)
        {
            MusicState = ShowMusicState.None;
            MusicMessage = null;
            return;
        }

        MusicMessage = _music.GetStatusMessage(show.Id);
        MusicState = _music.GetState(show.Id);
    }

    private ShowListItemViewModel? FindActive()
    {
        var last = _activeId is { } id ? Shows.FirstOrDefault(s => s.Id == id) : null;
        if (last is { IsRunning: true })
        {
            return last;
        }

        // A show started elsewhere (e.g. a Show widget) takes the bar.
        return Shows.FirstOrDefault(s => s.IsRunning) ?? last ?? SelectedShow;
    }

    private double CurrentPosition() => ActiveShow is not { } show
        ? 0
        : IsRunning ? ClampPosition(_playback.GetShowPosition(show.Id), show.LengthSeconds)
        : ClampPosition(_cueSeconds, show.LengthSeconds);

    private void Play(ShowListItemViewModel show)
    {
        // A cued point only applies to the stopped show in the bar; PlayShow ignores it when resuming a paused show.
        var from = show.Id == _activeId && !show.IsRunning ? _cueSeconds : 0;
        _activeId = show.Id;
        _playback.PlayShow(show.Id, ClampPosition(from, show.LengthSeconds));
        Refresh();
    }

    private void RebuildShows()
    {
        var project = _projects.Current;
        var byId = Shows.ToDictionary(s => s.Id);
        var index = 0;
        foreach (var show in project.Shows)
        {
            if (byId.Remove(show.Id, out var existing))
            {
                existing.Update(show);
                var at = Shows.IndexOf(existing);
                if (at != index)
                {
                    Shows.Move(at, index);
                }
            }
            else
            {
                Shows.Insert(index, new ShowListItemViewModel(show));
            }

            index++;
        }

        foreach (var removed in byId.Values)
        {
            // A deleted show can no longer be reached from the bar, so it must not keep playing unseen.
            if (_playback.IsShowRunning(removed.Id))
            {
                _playback.StopShow(removed.Id);
            }

            Shows.Remove(removed);
            if (removed.Id == _activeId)
            {
                _activeId = null;
            }
        }

        if (SelectedShow is not null && !Shows.Contains(SelectedShow))
        {
            SelectedShow = null;
        }

        SelectedShow ??= Shows.FirstOrDefault();
        OnPropertyChanged(nameof(HasShows));
        OnPropertyChanged(nameof(HasNoShows));
        OnPropertyChanged(nameof(EmptyBarText));
        Refresh();
        OnPropertyChanged(nameof(Progress));
    }

    private void Tick()
    {
        if (ActiveShow is null || !IsPlaying)
        {
            return;
        }

        if (!_isScrubbing)
        {
            Position = CurrentPosition();
        }

        IsOverridden = _playback.IsShowOverridden;
    }

    private void UpdateTicker(bool run)
    {
        if (run && _ticker is null)
        {
            _ticker = new CancellationTokenSource();
            _ = RunTickerAsync(_ticker.Token);
        }
        else if (!run && _ticker is not null)
        {
            _ticker.Cancel();
            _ticker.Dispose();
            _ticker = null;
        }
    }

    /// <summary>Started on the UI thread, so every tick continues there (no dispatcher type needed in the ViewModel).</summary>
    private async Task RunTickerAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by UpdateTicker: the show paused or stopped.
        }
    }
}
