using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DmxPro.App.Services;
using DmxPro.Audio;
using DmxPro.Audio.Analysis;
using DmxPro.Core.Shows;

namespace DmxPro.App.ViewModels;

/// <summary>
/// The music of the show selected on the Shows page: the audio lane (song block, waveform, status) and the music
/// settings in Properties. Add / replace / relink / remove / collect, volume, mute, fade-out and offset. Every change is
/// written to the model, marks the project changed and tells <see cref="IShowMusicService"/> so a playing song follows
/// at once. Analysis (waveform, length, tempo) runs in the background with progress and can be cancelled.
/// </summary>
public sealed partial class ShowAudioViewModel : ObservableObject
{
    /// <summary>Longest show the editor makes (also the cap when the song extends the show).</summary>
    private const double MaxShowSeconds = 24 * 3600;

    /// <summary>Lines of the Collect music confirmation before "… and N more".</summary>
    private const int MaxListedFiles = 12;

    private readonly ShowsViewModel _owner;
    private readonly ProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly IShowMusicService _music;

    private Show? _show;
    private AudioAnalysis? _analysis;

    /// <summary>File the current <see cref="_analysis"/> belongs to (skip re-analysing the same song).</summary>
    private string? _analysisPath;
    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource? _collectCts;

    /// <summary>An analysis failure the service did not report (shown on the lane).</summary>
    private string? _localError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing), nameof(IsError), nameof(HasProblem), nameof(StatusText))]
    private ShowMusicState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CancelAnalysisCommand))]
    private bool _isAnalyzing;

    /// <summary>Analysis progress 0..100.</summary>
    [ObservableProperty]
    private double _analysisProgress;

    [ObservableProperty]
    private float[]? _peaks;

    [ObservableProperty]
    private int _peaksPerSecond = 100;

    /// <summary>Where the lane draws the song (follows an offset drag before it is committed).</summary>
    [ObservableProperty]
    private double _laneOffsetSeconds;

    /// <summary>Song length the lane draws (0 = unknown).</summary>
    [ObservableProperty]
    private double _laneDurationSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(CollectMusicCommand), nameof(CancelCollectCommand))]
    private bool _isCollecting;

    [ObservableProperty]
    private string _collectText = string.Empty;

    public ShowAudioViewModel(ShowsViewModel owner, ProjectService projects, IDialogService dialogs, IFileDialogService files, IShowMusicService music)
    {
        _owner = owner;
        _projects = projects;
        _dialogs = dialogs;
        _files = files;
        _music = music;
    }

    public bool HasShow => _show is not null;

    public bool HasMusic => _show?.Audio is not null;

    public bool HasNoMusic => _show is not null && _show.Audio is null;

    public bool IsMissing => HasMusic && State == ShowMusicState.Missing;

    public bool IsError => HasMusic && (State == ShowMusicState.Error || _localError is not null);

    /// <summary>The song cannot play (missing or broken): a warning on the lane, in Properties and in the show picker.</summary>
    public bool HasProblem => IsMissing || IsError;

    public bool IsBusy => IsAnalyzing || IsCollecting;

    public string FileName => _show?.Audio is { } audio ? SafeFileName(audio.FilePath) : string.Empty;

    /// <summary>Label of the add / replace action.</summary>
    public string AddMusicHeader => HasMusic ? "Replace music…" : "Add music…";

    /// <summary>Lane header text: the song's file name, or "Music" without one.</summary>
    public string LaneTitle => HasMusic ? FileName : "Music";

    public string FilePathText => _show?.Audio is { } audio
        ? ResolvedPath() ?? audio.FilePath
        : "No music. Add a song with Add music… or drop a music file on the audio lane.";

    public string StatusText => !HasMusic ? string.Empty
        : _localError is { } local ? local
        : State == ShowMusicState.Missing ? StatusMessage ?? $"Music file not found: {FileName}. Relink it to play the music."
        : State == ShowMusicState.Error ? StatusMessage ?? $"\"{FileName}\" cannot be played."
        : string.Empty;

    /// <summary>Song volume 0..100 %.</summary>
    public double VolumePercent
    {
        get => (_show?.Audio?.Volume ?? 1) * 100;
        set
        {
            if (_show?.Audio is not { } audio || !double.IsFinite(value))
            {
                OnPropertyChanged();
                return;
            }

            var volume = Math.Round(ShowAudioTiming.ClampVolume(value / 100), 3);
            if (volume == audio.Volume)
            {
                return;
            }

            audio.Volume = volume;
            AudioSettingChanged();
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => string.Create(CultureInfo.CurrentCulture, $"{Math.Round(VolumePercent):0} %");

    public bool IsMuted
    {
        get => _show?.Audio?.Muted ?? false;
        set
        {
            if (_show?.Audio is not { } audio || audio.Muted == value)
            {
                return;
            }

            audio.Muted = value;
            AudioSettingChanged();
            OnPropertyChanged();
        }
    }

    /// <summary>Seconds of fade-out before the show ends.</summary>
    public double? FadeOutSeconds
    {
        get => _show?.Audio?.FadeOutSeconds;
        set
        {
            if (_show?.Audio is not { } audio || value is not { } v || !double.IsFinite(v))
            {
                OnPropertyChanged();
                return;
            }

            var fade = Math.Round(Math.Clamp(v, 0, Math.Max(0, _show.LengthSeconds)), 3);
            if (fade != audio.FadeOutSeconds)
            {
                audio.FadeOutSeconds = fade;
                AudioSettingChanged();
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Show time at which the song starts (negative skips the song's intro).</summary>
    public double? OffsetSeconds
    {
        get => _show?.Audio?.OffsetSeconds;
        set
        {
            if (value is { } v)
            {
                CommitOffset(v);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Lowest offset: the song may start before show time 0, but some of it must stay in the show.</summary>
    public double MinOffsetSeconds => -Math.Max(0, (_show?.Audio?.DurationSeconds ?? MaxShowSeconds) - 0.1);

    public double? DetectedBpm => _analysis is not null ? _analysis.Bpm : _show?.Audio?.DetectedBpm;

    public string DetectedBpmText => !HasMusic ? string.Empty
        : DetectedBpm is { } bpm ? string.Create(CultureInfo.CurrentCulture, $"{bpm:0.#} BPM")
        : _analysis is not null ? "No steady beat found"
        : IsAnalyzing ? "Analysing…"
        : "Not analysed";

    /// <summary>The beat grid differs from the song's detected tempo / first beat.</summary>
    public bool CanUseSongTempo =>
        _show is { Audio: { } audio } show
        && _analysis is { Bpm: { } bpm } analysis
        && (Math.Abs(show.Bpm - RoundBpm(bpm)) > 0.005
            || Math.Abs(show.BeatOffsetSeconds - SongGridOffset(audio, analysis)) > 0.0005);

    /// <summary>Called by the Shows page when the selected show changed or the project was reloaded.</summary>
    internal void Load(Show? show)
    {
        var sameSong = ReferenceEquals(show, _show) && show?.Audio is { } a && _analysisPath is not null
            && string.Equals(a.FilePath, _analysisPath, StringComparison.OrdinalIgnoreCase);
        if (!sameSong)
        {
            CancelAnalysisRun();
            _analysis = null;
            _analysisPath = null;
            _localError = null;
            Peaks = null;
        }

        _show = show;
        RefreshState();
        RefreshAll();
        if (show?.Audio is not null && !sameSong)
        {
            RelinkIfFoundElsewhere(show);
            _ = AnalyseAsync(show, isNewSong: false);
        }
    }

    /// <summary>Music state of any show (for the show picker).</summary>
    internal ShowMusicState GetState(Guid showId) => _music.GetState(showId);

    /// <summary>The music service's message for a show in trouble, or null.</summary>
    internal string? GetStatusMessage(Guid showId) => _music.GetStatusMessage(showId);

    /// <summary>The show's tempo or beat offset changed: "Use song tempo" may apply now (or no longer).</summary>
    internal void NotifyGridChanged() => OnPropertyChanged(nameof(CanUseSongTempo));

    /// <summary>The music service reported a state change for a show.</summary>
    internal void OnMusicStateChanged(Guid showId)
    {
        if (_show?.Id != showId)
        {
            return;
        }

        var wasMissing = IsMissing;
        RefreshState();

        // The file came back (or a relink elsewhere fixed it): load its waveform.
        if (wasMissing && !IsMissing && _show is { Audio: not null } show && _analysis is null && !IsAnalyzing)
        {
            _ = AnalyseAsync(show, isNewSong: false);
        }
    }

    /// <summary>Snaps a dragged song offset so the song's first beat (or its start) lands on the snap grid.</summary>
    public double SnapOffset(double rawOffsetSeconds)
    {
        var anchor = _analysis is { Bpm: not null } analysis ? analysis.FirstBeatSeconds : 0;
        return _owner.SnapSigned(rawOffsetSeconds + anchor) - anchor;
    }

    /// <summary>Live preview while dragging the song block (the model is unchanged until <see cref="CommitOffset"/>).</summary>
    public void PreviewOffset(double offsetSeconds)
    {
        if (HasMusic && double.IsFinite(offsetSeconds))
        {
            LaneOffsetSeconds = ClampOffset(offsetSeconds);
        }
    }

    /// <summary>Puts the lane back where the model says (cancelled drag).</summary>
    public void CancelOffsetPreview() => LaneOffsetSeconds = _show?.Audio?.OffsetSeconds ?? 0;

    /// <summary>Writes a new song offset (drag drop or the Offset box) and grows the show when the song now ends later.</summary>
    public void CommitOffset(double offsetSeconds)
    {
        if (_show is not { Audio: { } audio } show || !double.IsFinite(offsetSeconds))
        {
            CancelOffsetPreview();
            return;
        }

        var offset = Math.Round(ClampOffset(offsetSeconds), 3);
        LaneOffsetSeconds = offset;
        if (offset == audio.OffsetSeconds)
        {
            return;
        }

        audio.OffsetSeconds = offset;
        AudioSettingChanged();
        ExtendShowToFitSong(show);
        OnPropertyChanged(nameof(OffsetSeconds));
        OnPropertyChanged(nameof(CanUseSongTempo));
        _owner.OnShowTimingChanged();
    }

    /// <summary>A music file dropped on the audio lane: adds it, or replaces the current song after asking.</summary>
    public async Task DropFileAsync(string path)
    {
        if (_show is not { } show || IsCollecting)
        {
            return;
        }

        if (show.Audio is not null
            && !await _dialogs.ConfirmAsync(
                "Replace music",
                $"Replace \"{FileName}\" with \"{SafeFileName(path)}\" in \"{show.Name}\"?",
                "Replace"))
        {
            return;
        }

        await LinkAsync(show, path, relink: false);
    }

    /// <summary>True when the path has a music file extension the app plays (drag-and-drop feedback).</summary>
    public static bool IsSupportedFile(string path) => AudioFormats.IsSupported(path);

    // ───────────── Commands ─────────────

    /// <summary>Adds music to the show, or replaces its song.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AddMusicAsync()
    {
        if (_show is not { } show || _files.ShowOpenAudio(InitialDirectory()) is not { } path)
        {
            return;
        }

        await LinkAsync(show, path, relink: false);
    }

    /// <summary>Points the show at the song's new location, keeping offset, volume, mute and fade.</summary>
    [RelayCommand(CanExecute = nameof(CanEditMusic))]
    private async Task RelinkMusicAsync()
    {
        if (_show is not { Audio: not null } show || _files.ShowOpenAudio(InitialDirectory()) is not { } path)
        {
            return;
        }

        await LinkAsync(show, path, relink: true);
    }

    [RelayCommand(CanExecute = nameof(CanEditMusic))]
    private async Task RemoveMusicAsync()
    {
        if (_show is not { Audio: not null } show)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(
                "Remove music",
                $"Remove \"{FileName}\" from \"{show.Name}\"? The music file itself is not deleted.",
                "Remove")
            || !ReferenceEquals(_show, show))
        {
            return;
        }

        CancelAnalysisRun();
        show.Audio = null;
        _analysis = null;
        _analysisPath = null;
        _localError = null;
        Peaks = null;
        _projects.MarkChanged(affectsPlayback: false);
        _music.OnShowAudioChanged(show.Id);
        RefreshState();
        RefreshAll();
        _owner.OnShowMusicChanged(show);
    }

    /// <summary>Sets the beat grid to the song's detected tempo and first beat.</summary>
    [RelayCommand]
    private void UseSongTempo()
    {
        if (_show is { Audio: { } audio } show && _analysis is { Bpm: { } bpm } analysis)
        {
            ApplySongTempo(show, audio, bpm, analysis);
        }
    }

    [RelayCommand(CanExecute = nameof(IsAnalyzing))]
    private void CancelAnalysis() => CancelAnalysisRun();

    /// <summary>
    /// Copies every show's song into the project's Music folder (so the project can be moved or backed up with its
    /// music) and relinks the shows to the copies.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCollect))]
    private async Task CollectMusicAsync()
    {
        if (_projects.FilePath is not { } projectPath)
        {
            await _dialogs.ShowInfoAsync(
                "Collect music",
                "Save the project first: the music is copied into a \"Music\" folder next to the project file.");
            return;
        }

        var shows = _projects.Current.Shows;
        IReadOnlyList<(string Source, string Target)> plan;
        try
        {
            plan = ShowAudioPaths.PlanCollect(shows, projectPath, File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await _dialogs.ShowErrorAsync("Collect music", $"The music could not be collected: {ex.Message}");
            return;
        }

        var missing = shows.Count(s => s.Audio is not null && _music.GetState(s.Id) == ShowMusicState.Missing);
        var missingNote = missing > 0 ? $"\n\n{missing} show(s) have a missing song; relink those first." : string.Empty;
        if (plan.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Collect music", "All music is already in the project's Music folder." + missingNote);
            return;
        }

        var folder = Path.GetDirectoryName(plan[0].Target) ?? ShowAudioPaths.MusicFolderName;
        var lines = plan.Take(MaxListedFiles).Select(p =>
            SafeFileName(p.Source) == SafeFileName(p.Target) ? "• " + SafeFileName(p.Source) : $"• {SafeFileName(p.Source)} → {SafeFileName(p.Target)}");
        var more = plan.Count > MaxListedFiles ? $"\n… and {plan.Count - MaxListedFiles} more" : string.Empty;
        var message = $"Copy {plan.Count} music file(s) into\n{folder}\nand link the shows to the copies? The originals stay where they are.\n\n"
                      + string.Join("\n", lines) + more + missingNote;
        if (!await _dialogs.ConfirmAsync("Collect music", message, "Copy"))
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        _collectCts = cts;
        IsCollecting = true;
        var copied = new List<(string Source, string Target)>(plan.Count);
        var failures = new List<string>();
        try
        {
            for (var i = 0; i < plan.Count; i++)
            {
                if (cts.IsCancellationRequested)
                {
                    break;
                }

                var (source, target) = plan[i];
                CollectText = $"Copying {i + 1} of {plan.Count}: {SafeFileName(source)}";
                try
                {
                    await Task.Run(() => CopyFile(source, target, cts.Token), cts.Token);
                    copied.Add((source, target));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    failures.Add($"{SafeFileName(source)}: {ex.Message}");
                }
            }

            // Relink only what was copied completely; the originals are untouched.
            if (copied.Count > 0)
            {
                ShowAudioPaths.LinkCollected(shows, copied, projectPath, File.Exists);
                _projects.MarkChanged(affectsPlayback: false);
                foreach (var show in shows)
                {
                    if (show.Audio is not null)
                    {
                        _music.OnShowAudioChanged(show.Id);
                    }
                }

                if (_show?.Audio is { } audio)
                {
                    // The same song in a new place: keep its waveform.
                    _analysisPath = audio.FilePath;
                }

                RefreshAll();
            }
        }
        finally
        {
            _collectCts = null;
            IsCollecting = false;
            CollectText = string.Empty;
        }

        var summary = $"Copied {copied.Count} of {plan.Count} music file(s) into the Music folder. Save the project to keep the new links.";
        if (failures.Count > 0)
        {
            await _dialogs.ShowErrorAsync("Collect music", summary + "\n\nNot copied:\n" + string.Join("\n", failures.Take(MaxListedFiles)));
        }
        else if (copied.Count < plan.Count)
        {
            await _dialogs.ShowInfoAsync("Collect music", summary + " Cancelled.");
        }
        else
        {
            await _dialogs.ShowInfoAsync("Collect music", summary);
        }
    }

    [RelayCommand(CanExecute = nameof(IsCollecting))]
    private void CancelCollect() => _collectCts?.Cancel();

    private bool CanEdit() => _show is not null && !IsCollecting;

    private bool CanEditMusic() => _show?.Audio is not null && !IsCollecting;

    private bool CanCollect() => !IsCollecting;

    // ───────────── Linking and analysis ─────────────

    private async Task LinkAsync(Show show, string path, bool relink)
    {
        if (!AudioFormats.IsSupported(path))
        {
            await _dialogs.ShowErrorAsync(
                "Music",
                $"\"{SafeFileName(path)}\" is not a supported music file. Use MP3, WAV, M4A / AAC or FLAC.");
            return;
        }

        if (!File.Exists(path))
        {
            await _dialogs.ShowErrorAsync("Music", $"\"{path}\" was not found.");
            return;
        }

        // Work on a copy and swap it in, so a failure leaves the show's music as it was.
        var audio = show.Audio?.Clone() ?? new ShowAudio();
        try
        {
            ShowAudioPaths.Link(audio, path, _projects.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await _dialogs.ShowErrorAsync("Music", $"\"{SafeFileName(path)}\" cannot be linked: {ex.Message}");
            return;
        }

        if (!relink)
        {
            // A different song: its own length, tempo and start; volume, mute and fade-out stay.
            audio.OffsetSeconds = 0;
            audio.DurationSeconds = null;
            audio.DetectedBpm = null;
        }

        CancelAnalysisRun();
        show.Audio = audio;
        _analysis = null;
        _analysisPath = null;
        _localError = null;
        Peaks = null;
        _projects.MarkChanged(affectsPlayback: false);
        _music.OnShowAudioChanged(show.Id);
        RefreshState();
        RefreshAll();
        _owner.OnShowMusicChanged(show);
        await AnalyseAsync(show, isNewSong: !relink);
    }

    /// <summary>
    /// Loads the song's analysis (cache or background analysis) and applies it: length and tempo into the model,
    /// the show grows to fit the song, and for a newly added song the beat grid follows its tempo (asking first when
    /// items are already placed).
    /// </summary>
    private async Task AnalyseAsync(Show show, bool isNewSong)
    {
        CancelAnalysisRun();
        var cts = new CancellationTokenSource();
        _analysisCts = cts;
        var path = show.Audio?.FilePath;
        IsAnalyzing = true;
        AnalysisProgress = 0;
        OnPropertyChanged(nameof(DetectedBpmText));
        var progress = new Progress<double>(p =>
        {
            if (ReferenceEquals(_analysisCts, cts) && double.IsFinite(p))
            {
                AnalysisProgress = Math.Clamp(p, 0, 1) * 100;
            }
        });

        AudioAnalysis? result = null;
        try
        {
            result = await _music.GetAnalysisAsync(show, progress, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancelled by the user or replaced by another song / show.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The service reports errors through its state; this is a last line so a failure is never silent.
            if (ReferenceEquals(_show, show))
            {
                _localError = $"The music could not be analysed: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_analysisCts, cts))
            {
                _analysisCts = null;
                IsAnalyzing = false;
            }

            cts.Dispose();
        }

        // Another show, another song or no music any more: the result is stale.
        if (!ReferenceEquals(_show, show) || show.Audio is not { } audio || audio.FilePath != path)
        {
            return;
        }

        if (result is not null)
        {
            _analysis = result;
            _analysisPath = path;
            PeaksPerSecond = result.PeaksPerSecond > 0 ? result.PeaksPerSecond : 100;
            Peaks = result.Peaks;
            StoreAnalysis(audio, result);
            ExtendShowToFitSong(show);
            if (isNewSong && result.Bpm is { } bpm)
            {
                await OfferSongTempoAsync(show, bpm, result);
            }
        }

        RefreshState();
        RefreshAll();
        _owner.OnShowTimingChanged();
    }

    /// <summary>Keeps the stored length / tempo in step with the file (only marks the project changed when they differ).</summary>
    private void StoreAnalysis(ShowAudio audio, AudioAnalysis result)
    {
        var changed = false;
        if (double.IsFinite(result.DurationSeconds) && result.DurationSeconds > 0
            && (audio.DurationSeconds is not { } stored || Math.Abs(stored - result.DurationSeconds) > 0.001))
        {
            audio.DurationSeconds = Math.Round(result.DurationSeconds, 3);
            changed = true;
        }

        var bpm = result.Bpm is { } b && double.IsFinite(b) && b > 0 ? RoundBpm(b) : (double?)null;
        if (audio.DetectedBpm != bpm)
        {
            audio.DetectedBpm = bpm;
            changed = true;
        }

        if (changed)
        {
            _projects.MarkChanged(affectsPlayback: false);
        }
    }

    private async Task OfferSongTempoAsync(Show show, double bpm, AudioAnalysis analysis)
    {
        var audio = show.Audio!;
        var gridOffset = SongGridOffset(audio, analysis);
        if (Math.Abs(show.Bpm - RoundBpm(bpm)) <= 0.005 && Math.Abs(show.BeatOffsetSeconds - gridOffset) <= 0.0005)
        {
            return;
        }

        var hasItems = show.Tracks.Any(t => t.Items.Count > 0);
        if (hasItems)
        {
            var text = string.Create(
                CultureInfo.CurrentCulture,
                $"Use the song's tempo ({RoundBpm(bpm):0.#} BPM) for the beat grid?\n\nThe show uses {show.Bpm:0.#} BPM now. Items already on the timeline keep their times; only the beat and bar lines and snapping change.");
            if (!await _dialogs.ConfirmAsync("Song tempo", text, "Use song tempo"))
            {
                return;
            }

            if (!ReferenceEquals(_show, show) || !ReferenceEquals(show.Audio, audio))
            {
                return;
            }
        }

        ApplySongTempo(show, audio, bpm, analysis);
    }

    private void ApplySongTempo(Show show, ShowAudio audio, double bpm, AudioAnalysis analysis)
    {
        show.Bpm = RoundBpm(bpm);
        show.BeatOffsetSeconds = SongGridOffset(audio, analysis);
        _projects.MarkChanged();
        OnPropertyChanged(nameof(CanUseSongTempo));
        _owner.OnShowTimingChanged();
    }

    /// <summary>Grows the show so the whole song fits (never shrinks it).</summary>
    private void ExtendShowToFitSong(Show show)
    {
        var required = ShowAudioTiming.RequiredLength(show);
        if (required <= show.LengthSeconds + 0.0005)
        {
            return;
        }

        show.LengthSeconds = Math.Min(MaxShowSeconds, Math.Ceiling(required * 10) / 10);
        _projects.MarkChanged();
        _owner.OnShowTimingChanged();
    }

    /// <summary>When the song was found somewhere other than its stored path (moved with the project), store the new path.</summary>
    private void RelinkIfFoundElsewhere(Show show)
    {
        if (show.Audio is not { } audio || ResolvedPath() is not { } found
            || string.Equals(found, audio.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            ShowAudioPaths.Link(audio, found, _projects.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Keep the stored path; the music service still plays the resolved file.
            _localError = $"The music path could not be updated: {ex.Message}";
            return;
        }

        _projects.MarkChanged(affectsPlayback: false);
        _music.OnShowAudioChanged(show.Id);
        OnPropertyChanged(nameof(FilePathText));
    }

    // ───────────── Helpers ─────────────

    private void AudioSettingChanged()
    {
        _projects.MarkChanged(affectsPlayback: false);
        if (_show is { } show)
        {
            _music.OnShowAudioChanged(show.Id);
        }
    }

    private void RefreshState()
    {
        if (_show is { Audio: not null } show)
        {
            State = _music.GetState(show.Id);
            StatusMessage = _music.GetStatusMessage(show.Id);
        }
        else
        {
            State = ShowMusicState.None;
            StatusMessage = null;
        }

        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(StatusText));
        if (_show is { } current)
        {
            _owner.OnShowMusicChanged(current);
        }
    }

    private void RefreshAll()
    {
        var audio = _show?.Audio;
        LaneOffsetSeconds = audio?.OffsetSeconds ?? 0;
        LaneDurationSeconds = _analysis?.DurationSeconds is { } d && double.IsFinite(d) && d > 0 ? d : audio?.DurationSeconds ?? 0;
        OnPropertyChanged(string.Empty);
        AddMusicCommand.NotifyCanExecuteChanged();
        RelinkMusicCommand.NotifyCanExecuteChanged();
        RemoveMusicCommand.NotifyCanExecuteChanged();
    }

    private void CancelAnalysisRun()
    {
        if (_analysisCts is { } cts)
        {
            _analysisCts = null;
            cts.Cancel();
            IsAnalyzing = false;
        }
    }

    private string? ResolvedPath()
    {
        if (_show is not { } show)
        {
            return null;
        }

        try
        {
            return _music.ResolvePath(show);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private double ClampOffset(double offset) => Math.Clamp(offset, MinOffsetSeconds, MaxShowSeconds);

    private string? InitialDirectory()
    {
        if (_show?.Audio is { } audio && ResolvedPath() is { } current)
        {
            return Path.GetDirectoryName(current) ?? Path.GetDirectoryName(audio.FilePath);
        }

        return _projects.FilePath is { } project
            ? Path.GetDirectoryName(project)
            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    }

    private static void CopyFile(string source, string target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Copy under a temporary name first, so a failed or cancelled copy never leaves a half file under the real name.
        var partial = target + ".part";
        try
        {
            File.Copy(source, partial, overwrite: true);
            token.ThrowIfCancellationRequested();
            File.Move(partial, target, overwrite: false);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover ".part" file is harmless; the copy error itself is reported to the user.
        }
    }

    /// <summary>Show time of the song's first beat: where the beat grid starts when it follows the song.</summary>
    private static double SongGridOffset(ShowAudio audio, AudioAnalysis analysis) =>
        Math.Round(audio.OffsetSeconds + analysis.FirstBeatSeconds, 3);

    private static double RoundBpm(double bpm) => Math.Round(Math.Clamp(bpm, 20, 300), 2);

    private static string SafeFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
