using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using DmxPro.Core.Shows;
using DmxPro.Engine;

namespace DmxPro.App.ViewModels;

/// <summary>
/// Shows page: a timeline editor (tracks of function blocks placed in time) with Properties and the Function library
/// docked below. Functions placed in a show become the show's own copies (see <see cref="Project.PlaceFunctionInShow"/>).
/// The view translates mouse positions to seconds and calls the public methods here; every edit marks the project
/// changed. The show's music (audio lane, settings) lives in <see cref="Music"/>. The transport plays the show through
/// <see cref="IConsolePlayback"/> (the same player as the Virtual Console: one player show at a time; the music
/// follows it). While the show runs and the page is visible, the playhead follows the playback position (~30 Hz poll);
/// when stopped it is the start point for Play. The open function editor (a show's own copy) previews its draft on the
/// lights through <see cref="FunctionPreviewService"/> while the page is visible. In live mode
/// (<see cref="ILiveModeService"/>) only the Virtual Console drives the lights: Play / Resume are disabled here and a
/// hint says so (the engine refuses <see cref="ShowOrigin.Editor"/> shows anyway).
/// </summary>
public sealed partial class ShowsViewModel : PageViewModel
{
    /// <summary>Height of one track lane and its header, in pixels.</summary>
    public const double TrackHeight = 28;

    /// <summary>Gap between a lane's top edge and its item blocks, in pixels.</summary>
    public const double ItemTop = 3;

    /// <summary>Height of an item block: the lane minus its 1 px bottom border and a gap above and below.</summary>
    public const double ItemHeight = TrackHeight - 1 - (2 * ItemTop);

    /// <summary>Shortest item a drag or edit can make.</summary>
    public const double MinItemSeconds = 0.05;

    public const double MinZoom = 5;
    public const double MaxZoom = 200;

    /// <summary>Length of an item placed without a range selection, in bars of the show's BPM.</summary>
    private const int DefaultPlaceBars = 2;

    private const double TimelineTailPixels = 48;

    /// <summary>Fast forward / rewind: a held button jumps faster, up to this many seek steps per repeat.</summary>
    private const double MaxSkipAcceleration = 4;

    /// <summary>Playhead poll while the show plays and the page is visible (~30 Hz).</summary>
    private static readonly TimeSpan PlayheadPollInterval = TimeSpan.FromMilliseconds(33);

    private readonly ProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly AppSettingsService _settings;
    private readonly LightClipboardService _clipboard;
    private readonly IConsolePlayback _playback;
    private readonly FunctionPreviewService _preview;
    private readonly ILiveModeService _liveMode;
    private readonly SynchronizationContext? _uiContext;
    private double _timelineSeconds;
    private bool _reloading;

    /// <summary>The page is on screen (the playhead poll only runs then).</summary>
    private bool _isPageVisible;

    /// <summary>The selected show was running at the last transport refresh (to notice it stopped).</summary>
    private bool _wasRunning;

    /// <summary>Where the last Play started: Stop (and a show ending) return the playhead here.</summary>
    private double _playStartSeconds;

    /// <summary>The ruler is being dragged: the poll does not move the playhead meanwhile.</summary>
    private readonly IShowMusicService _music;
    private bool _isScrubbing;

    private CancellationTokenSource? _ticker;

    /// <summary><see cref="ProjectService.Revision"/> the lists were last rebuilt from (-1 = never).</summary>
    private long _loadedRevision = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShow), nameof(ShowLengthSeconds), nameof(ShowBpm), nameof(LengthText), nameof(BpmText))]
    [NotifyCanExecuteChangedFor(nameof(BeginRenameShowCommand), nameof(DeleteShowCommand), nameof(AddTrackCommand))]
    private Show? _selectedShow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRenamingShow))]
    private bool _isRenamingShow;

    [ObservableProperty]
    private string _showEditName = string.Empty;

    [ObservableProperty]
    private ShowsSnapOption _selectedSnap;

    [ObservableProperty]
    private double _pixelsPerSecond = 40;

    [ObservableProperty]
    private double _timelineWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayheadX), nameof(PlayheadText))]
    private double _playheadSeconds;

    /// <summary>Tempo the beat grid is drawn with (the selected show's BPM).</summary>
    [ObservableProperty]
    private double _gridBpm = Show.DefaultBpm;

    /// <summary>Show time of a beat the grid is counted from (the selected show's beat offset).</summary>
    [ObservableProperty]
    private double _gridOffsetSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(PlayPauseIcon), nameof(PlayPauseToolTip), nameof(TransportStateText))]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    private bool _isShowRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying), nameof(PlayPauseIcon), nameof(PlayPauseToolTip), nameof(TransportStateText))]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    private bool _isShowPaused;

    /// <summary>While playing, scroll the timeline so the playhead stays visible.</summary>
    [ObservableProperty]
    private bool _followPlayhead = true;

    /// <summary>The show picker's selection (kept in step with <see cref="SelectedShow"/>).</summary>
    [ObservableProperty]
    private ShowAudioPickerItem? _selectedShowChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItemPanelVisible), nameof(IsRangePanelVisible), nameof(IsShowPanelVisible))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedItemCommand), nameof(DuplicateSelectedItemCommand), nameof(EditSelectedItemCommand), nameof(SaveSelectedItemAsFunctionCommand))]
    private ShowItemViewModel? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRange), nameof(IsItemPanelVisible), nameof(IsRangePanelVisible), nameof(IsShowPanelVisible))]
    [NotifyCanExecuteChangedFor(nameof(CreateFunctionForRangeCommand))]
    private ShowTrackViewModel? _rangeTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private double _rangeStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private double _rangeEnd;

    [ObservableProperty]
    private string _newFunctionName = string.Empty;

    /// <summary>Type of the function "New function for this range" creates.</summary>
    [ObservableProperty]
    private FunctionTypeOption _newFunctionType;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaceSelectedFunctionCommand))]
    private ShowsLibraryItem? _selectedLibraryItem;

    [ObservableProperty]
    private ShowTrackViewModel? _activeTrack;

    /// <summary>Editor for the show's own copy of the selected item's function (any type); null when not editing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsNotEditing), nameof(IsTimelineVisible))]
    private FunctionEditorViewModel? _editor;

    /// <summary>Page info bar text (e.g. "Saved 'Red' to Functions."); shown while <see cref="IsNoticeOpen"/>.</summary>
    [ObservableProperty]
    private string _noticeMessage = string.Empty;

    [ObservableProperty]
    private bool _isNoticeOpen;

    public ShowsViewModel(
        ProjectService projects,
        IDialogService dialogs,
        AppSettingsService settings,
        LightClipboardService clipboard,
        IShowMusicService music,
        IConsolePlayback playback,
        IFileDialogService files,
        FunctionPreviewService preview,
        ILiveModeService liveMode)
    {
        _projects = projects;
        _dialogs = dialogs;
        _settings = settings;
        _clipboard = clipboard;
        _playback = playback;
        _preview = preview;
        _liveMode = liveMode;
        _uiContext = SynchronizationContext.Current;
        _music = music;
        Music = new ShowAudioViewModel(this, projects, dialogs, files, music);
        NewFunctionTypes = Enum.GetValues<FunctionType>()
            .Select(t => new FunctionTypeOption(t, FunctionRowViewModel.TypeDisplayName(t)))
            .ToList();
        _newFunctionType = NewFunctionTypes[0];
        SnapOptions =
        [
            new(TimelineSnapMode.Off, "Off"),
            new(TimelineSnapMode.Tenth, "0.1 s"),
            new(TimelineSnapMode.Beat, "Beat"),
            new(TimelineSnapMode.Bar, "Bar"),
        ];
        _selectedSnap = SnapOptions[2];
        _projects.ProjectChanged += (_, _) =>
        {
            Editor = null;
            Reload(keepShowId: null);
        };

        // Lights added/removed/renamed while a show-copy editor is open must show up in its Add list.
        _projects.ContentChanged += (_, _) => Editor?.RefreshPatch();

        // Raised on the UI thread (contract).
        music.StateChanged += (_, showId) => OnMusicStateChanged(showId);

        // May be raised on any thread.
        _playback.StateChanged += OnPlaybackStateChanged;

        // Raised on the UI thread (contract).
        liveMode.LiveModeChanged += (_, _) => OnLiveModeChanged();
        Reload(keepShowId: null);
    }

    public override string Title => "Shows";

    public override string Subtitle => "Build shows on a timeline: tracks with functions placed in time.";

    public ObservableCollection<Show> Shows { get; } = [];

    public ObservableCollection<ShowTrackViewModel> Tracks { get; } = [];

    /// <summary>The show picker's entries (show + music status).</summary>
    public ObservableCollection<ShowAudioPickerItem> ShowChoices { get; } = [];

    /// <summary>The selected show's music: audio lane and music settings.</summary>
    public ShowAudioViewModel Music { get; }

    public ObservableCollection<ShowsLibraryItem> Library { get; } = [];

    public IReadOnlyList<ShowsSnapOption> SnapOptions { get; }

    /// <summary>Every function type, for "New function for this range".</summary>
    public IReadOnlyList<FunctionTypeOption> NewFunctionTypes { get; }

    public bool HasShow => SelectedShow is not null;

    /// <summary>The scene editor covers the timeline; the page toolbar is disabled meanwhile.</summary>
    public bool IsEditing => Editor is not null;

    public bool IsNotEditing => Editor is null;

    /// <summary>Timeline + Properties/Library: there are shows and no scene is being edited.</summary>
    public bool IsTimelineVisible => HasShows && Editor is null;

    public bool IsNotRenamingShow => !IsRenamingShow;

    public bool HasShows => Shows.Count > 0;

    public bool HasNoShows => Shows.Count == 0;

    public bool HasTracks => Tracks.Count > 0;

    public bool HasNoTracks => HasShow && Tracks.Count == 0;

    public bool HasLibrary => Library.Count > 0;

    public bool HasNoLibrary => Library.Count == 0;

    public bool HasRange => RangeTrack is not null;

    public bool IsItemPanelVisible => SelectedItem is not null;

    public bool IsRangePanelVisible => SelectedItem is null && HasRange;

    public bool IsShowPanelVisible => SelectedItem is null && !HasRange && HasShow;

    public double PlayheadX => PlayheadSeconds * PixelsPerSecond;

    /// <summary>Playhead as m:ss.t (transport).</summary>
    public string PlayheadText => ShowPlayerViewModel.FormatPosition(PlayheadSeconds);

    public string LengthText => "/ " + ShowPlayerViewModel.FormatLength(SelectedShow?.LengthSeconds ?? 0);

    public bool IsPlaying => IsShowRunning && !IsShowPaused;

    public string PlayPauseIcon => IsPlaying ? "Pause" : "Play";

    public string PlayPauseToolTip => IsPlaying ? "Pause (Space)"
        : IsLive ? LiveModeHint
        : IsShowPaused ? "Resume (Space)"
        : "Play from the playhead (Space)";

    /// <summary>Live mode is on: only the Virtual Console drives the lights, so Play / Resume are disabled here.</summary>
    public bool IsLive => _liveMode.IsLive;

    /// <summary>Neutral hint next to the transport while <see cref="IsLive"/>.</summary>
    public static string LiveModeHint => "Live mode — shows play from the Virtual Console";

    public string TransportStateText => !IsShowRunning ? "Stopped" : IsShowPaused ? "Paused" : "Playing";

    public string BpmText => SelectedShow is null ? string.Empty : string.Create(CultureInfo.CurrentCulture, $"{SelectedShow.Bpm:0.#} BPM");

    public string RangeText => HasRange
        ? string.Create(CultureInfo.CurrentCulture, $"{FormatTime(RangeStart)} – {FormatTime(RangeEnd)}  ({RangeEnd - RangeStart:0.###} s) on {RangeTrack!.Name}")
        : string.Empty;

    /// <summary>Show length in seconds (Properties panel when nothing is selected).</summary>
    public double? ShowLengthSeconds
    {
        get => SelectedShow?.LengthSeconds;
        set
        {
            if (SelectedShow is null || value is not { } v || !double.IsFinite(v) || v <= 0)
            {
                OnPropertyChanged();
                return;
            }

            SelectedShow.LengthSeconds = Math.Round(Math.Min(v, 24 * 3600), 3);
            MarkChanged();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LengthText));
            UpdateTimelineGeometry(force: true);
        }
    }

    /// <summary>Show tempo; beats and bars for snapping and the beat lines come from it.</summary>
    public double? ShowBpm
    {
        get => SelectedShow?.Bpm;
        set
        {
            if (SelectedShow is null || value is not { } v || !double.IsFinite(v))
            {
                OnPropertyChanged();
                return;
            }

            SelectedShow.Bpm = Math.Round(Math.Clamp(v, 20, 300), 2);
            MarkChanged();
            OnShowTimingChanged();
        }
    }

    /// <summary>Show time of a beat the grid is counted from (bars start there); set from the song's first beat.</summary>
    public double? ShowBeatOffsetSeconds
    {
        get => SelectedShow?.BeatOffsetSeconds;
        set
        {
            if (SelectedShow is null || value is not { } v || !double.IsFinite(v))
            {
                OnPropertyChanged();
                return;
            }

            SelectedShow.BeatOffsetSeconds = Math.Round(Math.Clamp(v, -3600, 24 * 3600), 3);
            MarkChanged();
            OnShowTimingChanged();
        }
    }

    /// <summary>"mm:ss.fff".</summary>
    public static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds:000}");
    }

    /// <summary>"mm:ss".</summary>
    public static string FormatShort(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(Math.Max(0, seconds)));
        return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes:00}:{t.Seconds:00}");
    }

    /// <summary>Leaving the page stops the editor's light preview; it comes back when the page is shown again.</summary>
    public override void OnNavigatedFrom()
    {
        _isPageVisible = false;
        IsNoticeOpen = false;
        UpdateTicker();
        if (Editor is { } editor)
        {
            _preview.Detach(editor);
        }
    }

    public override void OnNavigatedTo()
    {
        _isPageVisible = true;
        RefreshTransport();
        // Rebuilding the lists and timeline on every visit made switching to this page slow; only rebuild when the
        // project changed.
        if (_loadedRevision != _projects.Revision)
        {
            Reload(SelectedShow?.Id);
        }

        Editor?.RefreshPatch();
        if (Editor is { } editor)
        {
            _preview.Attach(editor);
        }
    }

    /// <summary>
    /// Closes the scene editor, asking Save / Don't save / Cancel when it has unsaved changes. Returns false when the
    /// user cancelled (the editor stays open). The shell should call this before replacing the project or exiting.
    /// </summary>
    public async Task<bool> ConfirmCloseEditorAsync()
    {
        if (Editor is not { } editor)
        {
            return true;
        }

        if (editor.IsDirty)
        {
            switch (await _dialogs.AskSaveChangesAsync(editor.Original.Name))
            {
                case SaveChangesChoice.Cancel:
                    return false;
                case SaveChangesChoice.Save:
                    editor.Save();
                    break;
            }
        }

        Editor = null;
        return true;
    }

    /// <summary>Snaps a time to the current snap grid (beats/bars use the show's BPM and beat offset); never below 0.</summary>
    public double Snap(double seconds) =>
        TimelineSnap.Snap(seconds, SelectedSnap.Mode, SelectedShow?.Bpm ?? Show.DefaultBpm, SelectedShow?.BeatOffsetSeconds ?? 0);

    /// <summary>Like <see cref="Snap"/>, but times before 0 snap to the grid too (a song may start before the show).</summary>
    internal double SnapSigned(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return 0;
        }

        if (seconds >= 0)
        {
            return Snap(seconds);
        }

        var mode = SelectedSnap.Mode;
        var bpm = SelectedShow?.Bpm ?? Show.DefaultBpm;
        var step = TimelineSnap.StepSeconds(mode, bpm);
        var phase = mode is TimelineSnapMode.Beat or TimelineSnapMode.Bar
            ? TimelineSnap.GridOrigin(bpm, SelectedShow?.BeatOffsetSeconds ?? 0) % step
            : 0;
        return Math.Round((Math.Round((seconds - phase) / step, MidpointRounding.AwayFromZero) * step) + phase, 6);
    }

    public void SelectItem(ShowItemViewModel? item)
    {
        if (item is not null)
        {
            ClearRange();
            ActiveTrack = item.Track;
        }

        SelectedItem = item;
    }

    /// <summary>Clears the item and range selection (Esc, click on an empty lane).</summary>
    [RelayCommand]
    public void ClearSelection()
    {
        SelectedItem = null;
        ClearRange();
    }

    public void SetActiveTrack(ShowTrackViewModel? track) => ActiveTrack = track;

    /// <summary>Ruler drag started: the playhead follows the pointer only (no seek per mouse move).</summary>
    public void BeginScrub() => _isScrubbing = true;

    /// <summary>Ruler drag: moves the playhead on screen only.</summary>
    public void ScrubTo(double seconds) => PlayheadSeconds = ClampToShow(seconds);

    /// <summary>Ruler released (or capture lost): one seek to where it was let go.</summary>
    public void EndScrub(double seconds)
    {
        if (!_isScrubbing)
        {
            return;
        }

        _isScrubbing = false;
        Seek(seconds);
    }

    /// <summary>
    /// Moves the playhead (clamped to the show): a seek while the show runs (lights and music follow), else the start
    /// point for Play.
    /// </summary>
    public void Seek(double seconds)
    {
        var target = ClampToShow(seconds);
        if (SelectedShow is { } show && IsShowRunning)
        {
            _playback.SeekShow(show.Id, target);
        }

        PlayheadSeconds = target;
    }

    /// <summary>
    /// Fast rewind / forward by the seek step (Settings). <paramref name="repeat"/> = how many jumps of a held button
    /// came before: holding accelerates up to <see cref="MaxSkipAcceleration"/> steps per jump.
    /// </summary>
    public void SkipBy(int direction, int repeat)
    {
        var step = Math.Clamp(_settings.Current.SeekStepSeconds, AppSettings.MinSeekStepSeconds, AppSettings.MaxSeekStepSeconds);
        var factor = Math.Min(MaxSkipAcceleration, 1 + (Math.Max(0, repeat) / 6.0));
        var from = SelectedShow is { } show && IsShowRunning ? _playback.GetShowPosition(show.Id) : PlayheadSeconds;
        Seek(from + (Math.Sign(direction) * step * factor));
    }

    /// <summary>Timing changed (offset, tempo, beat offset, length): redraw grid and ruler, re-read the fields.</summary>
    internal void OnShowTimingChanged()
    {
        GridBpm = SelectedShow?.Bpm ?? Show.DefaultBpm;
        GridOffsetSeconds = SelectedShow?.BeatOffsetSeconds ?? 0;
        OnPropertyChanged(nameof(ShowBpm));
        OnPropertyChanged(nameof(BpmText));
        OnPropertyChanged(nameof(ShowBeatOffsetSeconds));
        OnPropertyChanged(nameof(ShowLengthSeconds));
        OnPropertyChanged(nameof(LengthText));
        Music.NotifyGridChanged();
        UpdateTimelineGeometry(force: false);
    }

    /// <summary>A show's music or its status changed: refresh its entry in the show picker.</summary>
    internal void OnShowMusicChanged(Show show)
    {
        foreach (var choice in ShowChoices)
        {
            if (ReferenceEquals(choice.Show, show))
            {
                choice.MusicProblem = MusicProblemOf(show);
                return;
            }
        }
    }

    /// <summary>Selects the time range [a, b] (any order) on a track; an empty range clears it.</summary>
    public void SetRange(ShowTrackViewModel track, double a, double b)
    {
        var start = Math.Max(0, Math.Min(a, b));
        var end = Math.Max(a, b);
        if (end - start < MinItemSeconds / 2)
        {
            ClearRange();
            return;
        }

        SelectedItem = null;
        if (RangeTrack is not null && !ReferenceEquals(RangeTrack, track))
        {
            RangeTrack.HasRange = false;
        }

        if (RangeTrack is null)
        {
            NewFunctionName = UniqueFunctionName(NewFunctionBaseName());
        }

        RangeTrack = track;
        ActiveTrack = track;
        RangeStart = start;
        RangeEnd = end;
        track.HasRange = true;
        track.RangeLeft = start * PixelsPerSecond;
        track.RangeWidth = (end - start) * PixelsPerSecond;
        OnPropertyChanged(nameof(RangeText));
    }

    public void ClearRange()
    {
        if (RangeTrack is not null)
        {
            RangeTrack.HasRange = false;
        }

        RangeTrack = null;
    }

    /// <summary>Marks the lane under the pointer during a drag (null clears the highlight).</summary>
    public void SetDropTarget(ShowTrackViewModel? track)
    {
        foreach (var t in Tracks)
        {
            t.IsDropTarget = ReferenceEquals(t, track);
        }
    }

    /// <summary>Drop of a drag (move or resize): writes the timing and, if needed, moves the item to another track.</summary>
    public void CommitItemMove(ShowItemViewModel item, ShowTrackViewModel targetTrack, double startSeconds, double lengthSeconds)
    {
        SetDropTarget(null);
        if (SelectedShow is null)
        {
            return;
        }

        var start = Math.Round(Math.Max(0, startSeconds), 3);
        var length = Math.Round(Math.Max(MinItemSeconds, lengthSeconds), 3);
        var sameTrack = ReferenceEquals(item.Track, targetTrack);
        if (sameTrack && start == item.Item.StartSeconds && length == item.Item.LengthSeconds)
        {
            item.Refresh(PixelsPerSecond, ColorOf(item.Function));
            return;
        }

        _projects.Current.MoveShowItem(SelectedShow.Id, item.Item.Id, targetTrack.Track.Id, start, length);
        MarkChanged();
        if (sameTrack)
        {
            item.Refresh(PixelsPerSecond, ColorOf(item.Function));
            UpdateTimelineGeometry(force: false);
            NotifyItemProperties();
        }
        else
        {
            RebuildTimeline(item.Item.Id);
        }
    }

    /// <summary>Places a library function on a track (the show gets its own copy), then selects the new item.</summary>
    public void PlaceFunction(Guid functionId, ShowTrackViewModel track, double startSeconds, double? lengthSeconds = null)
    {
        if (SelectedShow is null || _projects.Current.FindFunction(functionId) is null)
        {
            return;
        }

        var length = lengthSeconds ?? DefaultPlaceLength();
        var item = _projects.Current.PlaceFunctionInShow(
            SelectedShow.Id, track.Track.Id, functionId, Math.Round(Math.Max(0, startSeconds), 3), Math.Round(Math.Max(MinItemSeconds, length), 3));
        MarkChanged();
        RebuildTimeline(item.Id);
    }

    // ───────────── Called by the item / track view models (Properties panel, lane headers) ─────────────

    internal void SetItemTiming(ShowItemViewModel vm, double? start = null, double? length = null, double? fadeIn = null, double? fadeOut = null)
    {
        var item = vm.Item;
        static bool Bad(double? v) => v is { } x && !double.IsFinite(x);
        if (Bad(start) || Bad(length) || Bad(fadeIn) || Bad(fadeOut))
        {
            NotifyItemProperties();
            return;
        }

        item.SetTiming(
            Math.Round(Math.Max(0, start ?? item.StartSeconds), 3),
            Math.Round(Math.Max(MinItemSeconds, length ?? item.LengthSeconds), 3),
            Math.Round(fadeIn ?? item.FadeInSeconds, 3),
            Math.Round(fadeOut ?? item.FadeOutSeconds, 3));
        MarkChanged();
        vm.Refresh(PixelsPerSecond, ColorOf(vm.Function));
        UpdateTimelineGeometry(force: false);
    }

    internal void RenameItemFunction(ShowItemViewModel vm, string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (vm.Function is null || trimmed.Length == 0 || trimmed == vm.Function.Name)
        {
            vm.Refresh(PixelsPerSecond, ColorOf(vm.Function));
            return;
        }

        vm.Function.Name = trimmed;
        _projects.MarkChanged(affectsPlayback: false);
        RefreshItemsOf(vm.Function);
        if (vm.Function.ShowInFunctionList)
        {
            RebuildLibrary();
        }
    }

    internal void SetItemShowInFunctionList(ShowItemViewModel vm, bool show)
    {
        if (vm.Function is null || vm.Function.ShowInFunctionList == show)
        {
            return;
        }

        _projects.Current.SetShowInFunctionList(vm.Function.Id, show);
        _projects.MarkChanged(affectsPlayback: false);
        vm.Refresh(PixelsPerSecond, ColorOf(vm.Function));
        RebuildLibrary();
    }

    internal void RenameTrack(ShowTrackViewModel track, string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed == track.Track.Name)
        {
            return;
        }

        track.Track.Name = trimmed;
        _projects.MarkChanged(affectsPlayback: false);
        track.RefreshName();
        OnPropertyChanged(nameof(RangeText));
    }

    /// <summary>Header menu "Insert track above / below": a new empty track next to <paramref name="track"/>, made active.</summary>
    internal void InsertTrackNextTo(ShowTrackViewModel track, bool below)
    {
        if (SelectedShow is not { } show)
        {
            return;
        }

        var index = show.Tracks.IndexOf(track.Track);
        if (index < 0)
        {
            return;
        }

        var inserted = _projects.Current.InsertTrack(show.Id, below ? index + 1 : index);
        MarkChanged();
        RebuildTimeline(SelectedItem?.Item.Id);
        ActiveTrack = Tracks.FirstOrDefault(t => t.Track == inserted);
    }

    internal async Task DeleteTrackAsync(ShowTrackViewModel track)
    {
        if (SelectedShow is null)
        {
            return;
        }

        var count = track.Track.Items.Count;
        if (count > 0)
        {
            var what = count == 1 ? "its item" : $"its {count} items";
            if (!await _dialogs.ConfirmAsync("Delete track", $"Delete track \"{track.Name}\" and {what}?", "Delete"))
            {
                return;
            }
        }

        _projects.Current.RemoveTrack(SelectedShow.Id, track.Track.Id);
        MarkChanged();
        RebuildTimeline(keepItemId: null);
        RebuildLibrary();
    }

    // ───────────── Toolbar / panel commands ─────────────

    [RelayCommand]
    private void NewShow()
    {
        var show = _projects.Current.CreateShow(UniqueName("Show", _projects.Current.Shows.Select(s => s.Name)));
        MarkChanged();
        Reload(show.Id);
    }

    [RelayCommand(CanExecute = nameof(HasShow))]
    private void BeginRenameShow()
    {
        ShowEditName = SelectedShow!.Name;
        IsRenamingShow = true;
    }

    [RelayCommand]
    private void CommitRenameShow()
    {
        if (!IsRenamingShow)
        {
            return;
        }

        IsRenamingShow = false;
        var trimmed = ShowEditName.Trim();
        if (SelectedShow is null || trimmed.Length == 0 || trimmed == SelectedShow.Name)
        {
            return;
        }

        SelectedShow.Name = trimmed;
        _projects.MarkChanged(affectsPlayback: false);
        Reload(SelectedShow.Id);
    }

    [RelayCommand]
    private void CancelRenameShow() => IsRenamingShow = false;

    [RelayCommand(CanExecute = nameof(HasShow))]
    private async Task DeleteShowAsync()
    {
        var show = SelectedShow!;
        var message = $"Delete show \"{show.Name}\"? Its own function copies are deleted too, except those ticked \"Show in function list\".";
        if (!await _dialogs.ConfirmAsync("Delete show", message, "Delete"))
        {
            return;
        }

        var index = Shows.IndexOf(show);
        _projects.Current.DeleteShow(show.Id);
        MarkChanged();
        var next = _projects.Current.Shows.Count == 0 ? null : _projects.Current.Shows[Math.Min(index, _projects.Current.Shows.Count - 1)];
        Reload(next?.Id);
    }

    /// <summary>Adds an empty track at the end (toolbar, empty state, track header menus).</summary>
    [RelayCommand(CanExecute = nameof(HasShow))]
    private void AddTrack()
    {
        var track = _projects.Current.InsertTrack(SelectedShow!.Id, SelectedShow.Tracks.Count);
        MarkChanged();
        RebuildTimeline(SelectedItem?.Item.Id);
        ActiveTrack = Tracks.FirstOrDefault(t => t.Track == track);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedItem))]
    private void DeleteSelectedItem()
    {
        var item = SelectedItem!;
        _projects.Current.RemoveShowItem(SelectedShow!.Id, item.Item.Id);
        MarkChanged();
        RebuildTimeline(keepItemId: null);
        RebuildLibrary();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedItem))]
    private void DuplicateSelectedItem()
    {
        var copy = _projects.Current.DuplicateShowItem(SelectedShow!.Id, SelectedItem!.Item.Id);
        MarkChanged();
        RebuildTimeline(copy.Id);
    }

    /// <summary>
    /// Creates a show-owned function of the chosen type for the selected range, places it there and opens its editor
    /// (ui-plan: "pick type, the editor opens").
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasRange))]
    private async Task CreateFunctionForRangeAsync()
    {
        if (SelectedShow is null || RangeTrack is null)
        {
            return;
        }

        var type = NewFunctionType.Type ?? FunctionType.Scene;
        var name = NewFunctionName.Trim();
        if (name.Length == 0)
        {
            name = UniqueFunctionName(NewFunctionBaseName());
        }

        var item = _projects.Current.CreateFunctionInShow(
            SelectedShow.Id, RangeTrack.Track.Id, type, name, Math.Round(RangeStart, 3), Math.Round(RangeEnd - RangeStart, 3));
        MarkChanged();
        RebuildTimeline(item.Id);
        await EditSelectedItemAsync();
    }

    /// <summary>Places the selected library function at the selected range, or at the playhead on the active track.</summary>
    [RelayCommand(CanExecute = nameof(CanPlaceSelectedFunction))]
    private void PlaceSelectedFunction()
    {
        var function = SelectedLibraryItem!;
        if (RangeTrack is { } rangeTrack)
        {
            PlaceFunction(function.Id, rangeTrack, RangeStart, RangeEnd - RangeStart);
            return;
        }

        var track = ActiveTrack ?? SelectedItem?.Track ?? Tracks.FirstOrDefault();
        if (track is null)
        {
            return;
        }

        PlaceFunction(function.Id, track, PlayheadSeconds);
    }

    /// <summary>
    /// Opens the editor of the selected item's function (any type): the show's own copy, so the library original never
    /// changes. Edits stay on screen until saved.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditSelectedFunction))]
    private async Task EditSelectedItemAsync()
    {
        if (SelectedItem?.Function is not { } function || ReferenceEquals(Editor?.Original, function))
        {
            return;
        }

        if (!await ConfirmCloseEditorAsync())
        {
            return;
        }

        Editor = FunctionEditorFactory.Create(_projects.Current, function, _settings, _clipboard);
    }

    [RelayCommand]
    private async Task CloseEditorAsync() => await ConfirmCloseEditorAsync();

    /// <summary>
    /// "Save as function…": saves a copy of the selected block's function (the show's own copy, with every change made
    /// in the show) to the Functions list as a new library function under a name the user picks. The block keeps
    /// playing its show copy, unchanged.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditSelectedFunction))]
    private void SaveSelectedItemAsFunction()
    {
        if (SelectedItem?.Function is not { } function)
        {
            return;
        }

        var project = _projects.Current;
        var dialog = new SaveFunctionDialogViewModel(
            "Save as function",
            $"Saves a copy of \"{function.Name}\" as it is in this show to the Functions list. The show keeps its own copy.",
            project.SuggestFunctionName(function.Name),
            showFades: false,
            project.IsFunctionNameTaken);
        if (!_dialogs.ShowSaveFunction(dialog) || project.FindFunction(function.Id) is null)
        {
            return;
        }

        var saved = project.DuplicateFunction(function.Id, dialog.TrimmedName);
        MarkChanged();
        RebuildLibrary();
        SelectedLibraryItem = Library.FirstOrDefault(l => l.Id == saved.Id);
        ShowNotice($"Saved '{saved.Name}' to Functions.");
    }

    private void ShowNotice(string message)
    {
        NoticeMessage = message;

        // Re-open even when a previous notice is still showing, so the info bar announces the new text.
        IsNoticeOpen = false;
        IsNoticeOpen = true;
    }

    private bool CanEditSelectedFunction() => SelectedItem?.Function is not null && SelectedShow is not null;

    private bool CanEditSelectedItem() => SelectedItem is not null && SelectedShow is not null;

    private bool CanPlaceSelectedFunction() => SelectedLibraryItem is not null && SelectedShow is not null;

    // ───────────── Property change hooks ─────────────

    partial void OnEditorChanged(FunctionEditorViewModel? oldValue, FunctionEditorViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Saved -= OnEditorSaved;
            oldValue.CloseRequested -= OnEditorCloseRequested;
            _preview.Detach(oldValue);
            oldValue.Detach();
        }

        if (newValue is not null)
        {
            newValue.Saved += OnEditorSaved;
            newValue.CloseRequested += OnEditorCloseRequested;
            if (_isPageVisible)
            {
                _preview.Attach(newValue);
            }
        }
    }

    /// <summary>A different type: suggest a matching name unless the user typed their own.</summary>
    partial void OnNewFunctionTypeChanged(FunctionTypeOption? oldValue, FunctionTypeOption newValue)
    {
        // The ComboBox pushes null when its selected item disappears.
        if (newValue is null)
        {
            NewFunctionType = NewFunctionTypes[0];
            return;
        }

        var oldBase = FunctionFactory.DefaultBaseName(oldValue?.Type ?? FunctionType.Scene);
        if (string.IsNullOrWhiteSpace(NewFunctionName) || NewFunctionName.StartsWith(oldBase + " ", StringComparison.Ordinal))
        {
            NewFunctionName = UniqueFunctionName(NewFunctionBaseName());
        }
    }

    partial void OnSelectedShowChanged(Show? value)
    {
        if (_reloading)
        {
            return;
        }

        IsRenamingShow = false;
        OnShowSwitched();
        RebuildTimeline(keepItemId: null);

        // Open the song ahead of time so ▶ starts the music right on the first beat.
        if (value is not null)
        {
            _music.Preload(value.Id);
        }
    }

    /// <summary>The picker's choice drives <see cref="SelectedShow"/> (the rest of the page works with the show).</summary>
    partial void OnSelectedShowChoiceChanged(ShowAudioPickerItem? value)
    {
        // The ComboBox pushes null while its list is rebuilt.
        if (_reloading || value is null || ReferenceEquals(value.Show, SelectedShow))
        {
            return;
        }

        SelectedShow = value.Show;
    }

    partial void OnSelectedItemChanged(ShowItemViewModel? oldValue, ShowItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    partial void OnActiveTrackChanged(ShowTrackViewModel? oldValue, ShowTrackViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }

    partial void OnPixelsPerSecondChanged(double value)
    {
        var clamped = Math.Clamp(double.IsFinite(value) ? value : 40, MinZoom, MaxZoom);
        if (clamped != value)
        {
            PixelsPerSecond = clamped;
            return;
        }

        UpdateTimelineGeometry(force: true);
        foreach (var track in Tracks)
        {
            foreach (var item in track.Items)
            {
                item.Layout(item.Item.StartSeconds, item.Item.LengthSeconds, value);
            }

            if (track.HasRange)
            {
                track.RangeLeft = RangeStart * value;
                track.RangeWidth = (RangeEnd - RangeStart) * value;
            }
        }

        OnPropertyChanged(nameof(PlayheadX));
    }

    // ───────────── Rebuilding ─────────────

    private void Reload(Guid? keepShowId)
    {
        _loadedRevision = _projects.Revision;
        _reloading = true;
        try
        {
            Shows.Clear();
            ShowChoices.Clear();
            foreach (var show in _projects.Current.Shows)
            {
                Shows.Add(show);
                ShowChoices.Add(new ShowAudioPickerItem(show) { MusicProblem = MusicProblemOf(show) });
            }

            var target = (keepShowId is { } id ? Shows.FirstOrDefault(s => s.Id == id) : null) ?? Shows.FirstOrDefault();

            // Re-assign even when unchanged so the ComboBox re-reads a renamed show.
            SelectedShow = null;
            SelectedShow = target;
        }
        finally
        {
            _reloading = false;
        }

        IsRenamingShow = false;
        OnShowSwitched();

        // The edited scene was deleted (with its show or item): nothing is left to edit.
        if (Editor is not null && !_projects.Current.Functions.Contains(Editor.Original))
        {
            Editor = null;
        }

        OnPropertyChanged(nameof(HasShows));
        OnPropertyChanged(nameof(HasNoShows));
        OnPropertyChanged(nameof(IsTimelineVisible));
        RebuildLibrary();
        RebuildTimeline(SelectedItem?.Item.Id);
    }

    private void RebuildLibrary()
    {
        var keep = SelectedLibraryItem?.Id;
        Library.Clear();
        foreach (var function in _projects.Current.FunctionListItems.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Library.Add(new ShowsLibraryItem(function, _projects.Current));
        }

        SelectedLibraryItem = Library.FirstOrDefault(l => l.Id == keep);
        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(HasNoLibrary));
    }

    private void RebuildTimeline(Guid? keepItemId)
    {
        var activeTrackId = ActiveTrack?.Track.Id;
        var rangeTrackId = RangeTrack?.Track.Id;
        var (rangeStart, rangeEnd) = (RangeStart, RangeEnd);

        SelectedItem = null;
        RangeTrack = null;
        ActiveTrack = null;
        Tracks.Clear();

        ShowItemViewModel? keep = null;
        if (SelectedShow is { } show)
        {
            // One lookup table instead of a scan of every function per item (show copies make the list long).
            var functions = new Dictionary<Guid, LightFunction>(_projects.Current.Functions.Count);
            foreach (var function in _projects.Current.Functions)
            {
                functions.TryAdd(function.Id, function);
            }

            foreach (var track in show.Tracks)
            {
                var trackVm = new ShowTrackViewModel(this, track);
                foreach (var item in track.Items)
                {
                    var function = functions.GetValueOrDefault(item.FunctionId);
                    var itemVm = new ShowItemViewModel(this, trackVm, item, function, ColorOf(function));
                    itemVm.Layout(item.StartSeconds, item.LengthSeconds, PixelsPerSecond);
                    trackVm.Items.Add(itemVm);
                    if (item.Id == keepItemId)
                    {
                        keep = itemVm;
                    }
                }

                Tracks.Add(trackVm);
            }
        }

        UpdateTimelineGeometry(force: true);
        ActiveTrack = Tracks.FirstOrDefault(t => t.Track.Id == activeTrackId);
        if (keep is not null)
        {
            SelectItem(keep);
        }
        else if (Tracks.FirstOrDefault(t => t.Track.Id == rangeTrackId) is { } rangeTrack)
        {
            SetRange(rangeTrack, rangeStart, rangeEnd);
        }

        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(HasNoTracks));
        OnPropertyChanged(nameof(PlayheadX));
    }

    /// <summary>
    /// Recomputes the timeline width (show length, item ends and the song's end) and the grid tempo; skipped when
    /// nothing changed. Ruler, beat grid and waveform draw themselves from these values (only the visible part).
    /// </summary>
    private void UpdateTimelineGeometry(bool force)
    {
        var seconds = SelectedShow?.LengthSeconds ?? 0;
        if (SelectedShow?.Audio is { } audio && ShowAudioTiming.SongEndSeconds(audio) is { } songEnd)
        {
            seconds = Math.Max(seconds, songEnd);
        }

        foreach (var track in Tracks)
        {
            foreach (var item in track.Items)
            {
                seconds = Math.Max(seconds, item.Item.EndSeconds);
            }
        }

        GridBpm = SelectedShow?.Bpm ?? Show.DefaultBpm;
        GridOffsetSeconds = SelectedShow?.BeatOffsetSeconds ?? 0;
        if (!force && seconds == _timelineSeconds)
        {
            return;
        }

        _timelineSeconds = seconds;
        TimelineWidth = (seconds * PixelsPerSecond) + TimelineTailPixels;
    }

    // ───────────── Transport ─────────────

    /// <summary>
    /// Play / pause the selected show (Space on the timeline). Play starts at the playhead. While live, Play and Resume
    /// are disabled (Pause stays available for a show that is playing).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        if (SelectedShow is not { } show)
        {
            return;
        }

        if (IsPlaying)
        {
            _playback.PauseShow(show.Id);
        }
        else if (IsShowRunning)
        {
            _playback.PlayShow(show.Id, origin: ShowOrigin.Editor);
        }
        else
        {
            // At (or past) the end, Play starts over instead of ending at once.
            var start = ClampToShow(PlayheadSeconds);
            if (start >= show.LengthSeconds - 0.05)
            {
                start = 0;
            }

            _playStartSeconds = start;
            PlayheadSeconds = start;
            _playback.PlayShow(show.Id, start, ShowOrigin.Editor);
        }

        RefreshTransport();
    }

    private bool CanPlayPause() => HasShow && (IsPlaying || !IsLive);

    /// <summary>Live mode switched: Play / Resume follow it; live-on stops the Shows-page show (the playback reports it).</summary>
    private void OnLiveModeChanged()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(PlayPauseToolTip));
        RefreshTransport();
        PlayPauseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Stops the show (lights release; the playhead returns to where Play started). When stopped: back to 0.</summary>
    [RelayCommand(CanExecute = nameof(HasShow))]
    private void Stop()
    {
        if (SelectedShow is not { } show)
        {
            return;
        }

        if (IsShowRunning)
        {
            _playback.StopShow(show.Id);
            RefreshTransport();
        }
        else
        {
            _playStartSeconds = 0;
            PlayheadSeconds = 0;
        }
    }

    /// <summary>Back to the show's start (Home).</summary>
    [RelayCommand(CanExecute = nameof(HasShow))]
    private void GoToStart() => Seek(0);

    /// <summary>A different show is selected (or the project reloaded): music, grid and transport follow it.</summary>
    private void OnShowSwitched()
    {
        SelectedShowChoice = ShowChoices.FirstOrDefault(c => ReferenceEquals(c.Show, SelectedShow));
        _wasRunning = false;
        _isScrubbing = false;
        _playStartSeconds = 0;
        PlayheadSeconds = 0;
        GridBpm = SelectedShow?.Bpm ?? Show.DefaultBpm;
        GridOffsetSeconds = SelectedShow?.BeatOffsetSeconds ?? 0;
        OnPropertyChanged(nameof(ShowBeatOffsetSeconds));
        Music.Load(SelectedShow);
        RefreshTransport();
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        GoToStartCommand.NotifyCanExecuteChanged();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            RefreshTransport();
        }
        else
        {
            _uiContext.Post(_ => RefreshTransport(), null);
        }
    }

    /// <summary>Re-reads running / paused / position of the selected show and starts or stops the playhead poll.</summary>
    private void RefreshTransport()
    {
        var running = false;
        var paused = false;
        if (SelectedShow is { } show)
        {
            running = _playback.IsShowRunning(show.Id);
            paused = running && _playback.IsShowPaused(show.Id);
            if (running && !_isScrubbing)
            {
                PlayheadSeconds = ClampToShow(_playback.GetShowPosition(show.Id));
            }
            else if (!running && _wasRunning && !_isScrubbing)
            {
                // Stopped here or elsewhere (console, Release All) or ended by itself.
                PlayheadSeconds = ClampToShow(_playStartSeconds);
            }
        }

        _wasRunning = running;
        IsShowRunning = running;
        IsShowPaused = paused;
        UpdateTicker();
    }

    private void UpdateTicker()
    {
        var run = _isPageVisible && IsShowRunning && !IsShowPaused;
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
        using var timer = new PeriodicTimer(PlayheadPollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                PollPlayhead();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by UpdateTicker: paused, stopped or the page was left.
        }
    }

    private void PollPlayhead()
    {
        if (SelectedShow is not { } show || _isScrubbing)
        {
            return;
        }

        if (!_playback.IsShowRunning(show.Id))
        {
            RefreshTransport();
            return;
        }

        PlayheadSeconds = ClampToShow(_playback.GetShowPosition(show.Id));
    }

    private double ClampToShow(double seconds) =>
        ShowPlayerViewModel.ClampPosition(seconds, SelectedShow?.LengthSeconds ?? 0);

    // ───────────── Music status ─────────────

    private void OnMusicStateChanged(Guid showId)
    {
        Music.OnMusicStateChanged(showId);
        foreach (var choice in ShowChoices)
        {
            if (choice.Show.Id == showId)
            {
                choice.MusicProblem = MusicProblemOf(choice.Show);
            }
        }
    }

    /// <summary>"Music missing: …" / "Music error: …" for the show picker, or null when the music is fine (or none).</summary>
    private string? MusicProblemOf(Show show)
    {
        if (show.Audio is null)
        {
            return null;
        }

        var state = Music.GetState(show.Id);
        return state switch
        {
            ShowMusicState.Missing => Music.GetStatusMessage(show.Id) ?? "Music file not found: relink it.",
            ShowMusicState.Error => Music.GetStatusMessage(show.Id) ?? "The music cannot be played.",
            _ => null,
        };
    }

    // ───────────── Helpers ─────────────

    private void MarkChanged() => _projects.MarkChanged();

    private void OnEditorSaved(object? sender, EventArgs e)
    {
        if (sender is not FunctionEditorViewModel editor)
        {
            return;
        }

        MarkChanged();
        RefreshItemsOf(editor.Original);
        NotifyItemProperties();
        RebuildLibrary();
    }

    /// <summary>The editor's own Close button goes through the same unsaved-changes prompt.</summary>
    private void OnEditorCloseRequested(object? sender, EventArgs e) => _ = ConfirmCloseEditorAsync();

    private RgbColor? ColorOf(LightFunction? function) => ShowsLibraryItem.PreviewColor(function, _projects.Current);

    private double DefaultPlaceLength() =>
        TimelineSnap.StepSeconds(TimelineSnapMode.Bar, SelectedShow?.Bpm ?? Show.DefaultBpm) * DefaultPlaceBars;

    private void RefreshItemsOf(LightFunction function)
    {
        foreach (var item in Tracks.SelectMany(t => t.Items).Where(i => ReferenceEquals(i.Function, function)))
        {
            item.Refresh(PixelsPerSecond, ColorOf(function));
        }
    }

    /// <summary>Re-reads the Properties panel fields of the selected item (e.g. after a drag).</summary>
    private void NotifyItemProperties() => SelectedItem?.Refresh(PixelsPerSecond, ColorOf(SelectedItem.Function));

    private string NewFunctionBaseName() => FunctionFactory.DefaultBaseName(NewFunctionType?.Type ?? FunctionType.Scene);

    private string UniqueFunctionName(string prefix) => UniqueName(prefix, _projects.Current.Functions.Select(f => f.Name));

    private static string UniqueName(string prefix, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        for (var n = 1; ; n++)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"{prefix} {n}");
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }
}
