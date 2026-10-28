using System.Globalization;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;
using DmxPro.Core.Validation;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace DmxPro.App.Services;

/// <summary>
/// Keeps the open project and its file in step while the app runs:
/// <list type="bullet">
/// <item><b>Live reload.</b> When another program (a script, another tool, an AI agent) changes the open project file,
/// its content is applied <i>in place</i> through the undo/redo restore path
/// (<see cref="ProjectService.TryApplyExternalContent"/>): not a project open, so output, live mode, running functions and
/// the Virtual Console stay as they are; it is one undo step ("Reloaded from disk"). The app's own saves are recognised
/// by the file's content hash (<see cref="ProjectService.DiskContentHash"/>) and ignored. With unsaved changes (or an
/// editor draft) the user is asked: Reload, or Keep mine (the next save overwrites the file).</item>
/// <item><b>Auto-save.</b> Every <see cref="AppSettings.AutosaveIntervalMinutes"/> a project that has a file and unsaved
/// changes is saved through the normal save path. Editor drafts are not saved (they are not in the project yet). It never
/// overwrites a disk change that was not applied yet, and waits while a dialog is open.</item>
/// </list>
/// Everything runs on the UI thread (file events and timers are posted to the context captured by <see cref="Start()"/>);
/// file reads are async and parsing runs on the thread pool, so the UI never blocks.
/// </summary>
public sealed class ProjectFileSyncService : IDisposable
{
    /// <summary>Quiet time after the last file event before the file is read (writers often touch it several times).</summary>
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>When an auto-save or a reload has to wait (a dialog is open, another file action runs), it tries again after this.</summary>
    public static readonly TimeSpan PostponeDelay = TimeSpan.FromSeconds(30);

    /// <summary>A reload that found a dialog open looks again after this.</summary>
    public static readonly TimeSpan ModalRecheckDelay = TimeSpan.FromSeconds(1);

    /// <summary>Name of the undo step a reload creates.</summary>
    public const string ReloadDescription = "Reloaded from disk";

    private readonly ProjectService _projects;
    private readonly ProjectSerializer _serializer;
    private readonly IDialogService _dialogs;
    private readonly InfoBarState _infoBar;
    private readonly AppSettingsService _settings;
    private readonly IReadOnlyList<IUnsavedDraftSource> _drafts;
    private readonly TimeProvider _time;
    private readonly ILogger<ProjectFileSyncService> _logger;

    private SynchronizationContext? _ui;
    private bool _started;
    private bool _stopped;
    private int _suspended;

    private IDisposable? _watcher;
    private string? _watchedPath;

    private ITimer? _debounce;
    private long _debounceId;
    private ITimer? _autoSaveTimer;
    private long _autoSaveId;

    // One file action at a time (check / reload / auto-save); a file event during one is looked at right after it.
    private bool _busy;
    private bool _checkAgain;
    private Task _work = Task.CompletedTask;

    // Disk content that could not be read (warned once), and disk content an auto-save already warned about.
    private string? _unreadableHash;
    private string? _autoSaveWarnedHash;

    private bool _appliedAutoSaveEnabled;
    private int _appliedInterval;
    private bool _appliedReload;

    public ProjectFileSyncService(
        ProjectService projects,
        ProjectSerializer serializer,
        IDialogService dialogs,
        InfoBarState infoBar,
        AppSettingsService settings,
        IEnumerable<IUnsavedDraftSource> drafts,
        TimeProvider time,
        ILogger<ProjectFileSyncService> logger)
    {
        _projects = projects;
        _serializer = serializer;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _settings = settings;
        _drafts = drafts.ToList();
        _time = time;
        _logger = logger;
    }

    /// <summary>The file action running now, or the last one (tests await it; it never faults).</summary>
    public Task Idle => _work;

    /// <summary>Creates the watcher for a path and calls the action (on any thread) when that file may have changed.</summary>
    internal Func<string, Action, IDisposable> WatcherFactory { get; set; } = CreateFileWatcher;

    /// <summary>Tries to read a file that is locked by its writer.</summary>
    internal int ReadAttempts { get; set; } = 5;

    /// <summary>Tries to parse a file that may still be half-written.</summary>
    internal int ParseAttempts { get; set; } = 4;

    /// <summary>Wait between read / parse attempts (zero in tests).</summary>
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The path being watched (null: none).</summary>
    internal string? WatchedPath => _watchedPath;

    private AppSettings Settings => _settings.Current;

    private TimeSpan AutoSaveInterval => TimeSpan.FromMinutes(Settings.AutosaveIntervalMinutes);

    /// <summary>Starts watching and the auto-save timer. Call once on the UI thread.</summary>
    public void Start() => Start(SynchronizationContext.Current);

    /// <summary>Starts with an explicit context for file events and timers (null: run them inline, for tests).</summary>
    internal void Start(SynchronizationContext? ui)
    {
        if (_started || _stopped)
        {
            return;
        }

        _started = true;
        _ui = ui;
        _projects.ProjectChanged += OnProjectChanged;
        _projects.StateChanged += OnProjectStateChanged;
        _settings.Changed += OnSettingsChanged;
        _appliedReload = Settings.ReloadProjectOnDiskChange;
        _appliedAutoSaveEnabled = Settings.AutoSaveEnabled;
        _appliedInterval = Settings.AutosaveIntervalMinutes;
        RearmWatcher();
        RestartAutoSaveTimer();
    }

    /// <summary>
    /// Pauses auto-save and reloads until the returned token is disposed: used while the user answers "save changes?"
    /// and a project is replaced (New / Open), so an auto-save never writes changes the user just chose to discard.
    /// </summary>
    public IDisposable Suspend()
    {
        _suspended++;
        return new Suspension(this);
    }

    /// <summary>Stops for good (the app is closing): no more reloads or auto-saves.</summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        if (_started)
        {
            _projects.ProjectChanged -= OnProjectChanged;
            _projects.StateChanged -= OnProjectStateChanged;
            _settings.Changed -= OnSettingsChanged;
        }

        DisposeWatcher();
        CancelDebounce();
        CancelAutoSaveTimer();
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    /// <summary>
    /// Looks at the project file now (normally after a file event plus <see cref="DebounceDelay"/>): applies a change made
    /// by another program, asks first when there is unsaved work. Returns the running check.
    /// </summary>
    public Task CheckForExternalChangeAsync()
    {
        RequestCheck();
        return _work;
    }

    /// <summary>
    /// Auto-saves now when it is due: auto-save on, the project has a file and unsaved changes, no dialog open, and the
    /// file still holds what was last loaded or saved here. Called by the timer; never throws.
    /// </summary>
    public async Task<AutoSaveOutcome> AutoSaveAsync()
    {
        if (_stopped || !Settings.AutoSaveEnabled || _projects.FilePath is not { } path || !_projects.IsDirty)
        {
            return AutoSaveOutcome.NotNeeded;
        }

        if (_busy || _suspended > 0 || _dialogs.IsModalOpen)
        {
            return AutoSaveOutcome.Postponed;
        }

        _busy = true;
        var work = AutoSaveCoreAsync(path);
        _work = work;
        try
        {
            return await work;
        }
        finally
        {
            _busy = false;
            if (_checkAgain && !_stopped)
            {
                RequestCheck();
            }
        }
    }

    // ───────────── Auto-save ─────────────

    private async Task<AutoSaveOutcome> AutoSaveCoreAsync(string path)
    {
        var project = _projects.Current;
        var knownHash = _projects.DiskContentHash;
        try
        {
            // Never overwrite a change another program made that is not in the app yet.
            var disk = await ReadFileAsync(path);
            if (disk is not null && ProjectSerializer.ComputeContentHash(disk) is var diskHash && diskHash != knownHash)
            {
                OnAutoSaveFoundDiskChange(path, diskHash);
                return AutoSaveOutcome.DiskChanged;
            }

            // The read is async: the user may have saved, opened another project or opened a dialog meanwhile.
            if (!ReferenceEquals(project, _projects.Current) || _projects.FilePath != path || !_projects.IsDirty)
            {
                return AutoSaveOutcome.NotNeeded;
            }

            if (_projects.DiskContentHash != knownHash || _suspended > 0 || _dialogs.IsModalOpen)
            {
                return AutoSaveOutcome.Postponed;
            }

            await _projects.SaveAsync(path);
            _autoSaveWarnedHash = null;
            _infoBar.Show(InfoBarSeverity.Success, $"Auto-saved {_time.GetLocalNow().ToString("HH:mm", CultureInfo.CurrentCulture)}");
            return AutoSaveOutcome.Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Auto-save of {Path} failed: {Error}", path, ex.Message);
            _infoBar.Show(
                InfoBarSeverity.Error,
                $"Auto-save failed: {ex.Message} Your changes are not saved yet; save with Ctrl+S or Save as.");
            return AutoSaveOutcome.Failed;
        }
        catch (Exception ex)
        {
            // Never crash the app from a timer: report it visibly (safety rule 10).
            _logger.LogError(ex, "Auto-save of {Path} failed", path);
            _infoBar.Show(InfoBarSeverity.Error, $"Auto-save failed: {ex.Message} Your changes are not saved yet.");
            return AutoSaveOutcome.Failed;
        }
    }

    private void OnAutoSaveFoundDiskChange(string path, string diskHash)
    {
        if (Settings.ReloadProjectOnDiskChange)
        {
            // Let the reload flow handle it (it asks, because there are unsaved changes) right after this.
            _checkAgain = true;
            return;
        }

        if (_autoSaveWarnedHash != diskHash)
        {
            _autoSaveWarnedHash = diskHash;
            _infoBar.Show(
                InfoBarSeverity.Warning,
                $"Auto-save skipped: \"{Path.GetFileName(path)}\" was changed outside DMX Pro and live reload is off. " +
                "Save (Ctrl+S) to overwrite it with your version, or reopen the project to use the file's version.");
        }
    }

    private void RestartAutoSaveTimer()
    {
        if (Settings.AutoSaveEnabled)
        {
            ScheduleAutoSave(AutoSaveInterval);
        }
        else
        {
            CancelAutoSaveTimer();
        }
    }

    private void ScheduleAutoSave(TimeSpan due)
    {
        CancelAutoSaveTimer();
        if (_stopped)
        {
            return;
        }

        var id = ++_autoSaveId;
        _autoSaveTimer = _time.CreateTimer(_ => Post(() => OnAutoSaveDue(id)), null, due, Timeout.InfiniteTimeSpan);
    }

    private void OnAutoSaveDue(long id)
    {
        if (_stopped || id != _autoSaveId)
        {
            return;
        }

        _ = RunScheduledAutoSaveAsync();
    }

    private async Task RunScheduledAutoSaveAsync()
    {
        var outcome = await AutoSaveAsync();
        if (!_stopped && Settings.AutoSaveEnabled)
        {
            ScheduleAutoSave(outcome is AutoSaveOutcome.Postponed or AutoSaveOutcome.DiskChanged ? PostponeDelay : AutoSaveInterval);
        }
    }

    private void CancelAutoSaveTimer()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
        _autoSaveId++;
    }

    // ───────────── Live reload ─────────────

    private void RequestCheck()
    {
        if (_stopped)
        {
            return;
        }

        if (_busy)
        {
            _checkAgain = true;
            return;
        }

        _busy = true;
        _work = RunChecksAsync();
    }

    private async Task RunChecksAsync()
    {
        try
        {
            do
            {
                _checkAgain = false;
                await CheckOnceAsync();
            }
            while (_checkAgain && !_stopped);
        }
        catch (Exception ex)
        {
            // Never crash the app from a file event: report it visibly (safety rule 10).
            _logger.LogError(ex, "Checking the project file for outside changes failed");
            _infoBar.Show(InfoBarSeverity.Warning, $"Project file changed on disk but could not be read: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task CheckOnceAsync()
    {
        if (!Settings.ReloadProjectOnDiskChange || _projects.FilePath is not { } path)
        {
            return;
        }

        if (_suspended > 0 || _dialogs.IsModalOpen)
        {
            // Not under an open dialog (it may hold objects of the project) or while a project is being replaced.
            ScheduleCheck(ModalRecheckDelay);
            return;
        }

        var project = _projects.Current;
        var knownHash = _projects.DiskContentHash;
        string? hash = null;
        ProjectLoadResult? loaded = null;
        string? error = null;
        for (var attempt = 1; attempt <= Math.Max(ParseAttempts, 1); attempt++)
        {
            byte[]? bytes;
            try
            {
                bytes = await ReadFileAsync(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                break;
            }

            if (bytes is null)
            {
                // Gone for now (a writer that deletes and renames): its next event brings it back.
                return;
            }

            hash = ProjectSerializer.ComputeContentHash(bytes);
            if (hash == _projects.DiskContentHash || hash == _unreadableHash)
            {
                // Our own save, the same content again, or a broken version already reported.
                return;
            }

            try
            {
                loaded = await Task.Run(() => _serializer.Deserialize(bytes));
                break;
            }
            catch (Exception ex)
            {
                // Most likely still being written: wait and read again.
                error = ex is DmxFileFormatException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                if (attempt < ParseAttempts)
                {
                    await DelayAsync();
                }
            }
        }

        if (!IsStillCurrent(project, path, knownHash))
        {
            _checkAgain = true;
            return;
        }

        if (loaded is null || hash is null)
        {
            _unreadableHash = hash;
            _logger.LogWarning("Project file {Path} changed on disk but could not be read: {Error}", path, error);
            _infoBar.Show(
                InfoBarSeverity.Warning,
                $"Project file changed on disk but could not be read: {error} The project here is kept as it is; " +
                "auto-save waits until you save (Ctrl+S overwrites the file) or the file is fixed.");
            return;
        }

        _unreadableHash = null;
        if (_projects.IsDirty || HasUnsavedDraft())
        {
            var choice = await _dialogs.AskReloadExternalChangeAsync(Path.GetFileName(path));
            if (!IsStillCurrent(project, path, knownHash))
            {
                _checkAgain = true;
                return;
            }

            if (choice == ExternalChangeChoice.KeepMine)
            {
                _projects.AcknowledgeDiskContent(hash);
                _infoBar.Show(
                    InfoBarSeverity.Informational,
                    $"Kept your changes. \"{Path.GetFileName(path)}\" was changed outside DMX Pro; your next save overwrites it.");
                return;
            }
        }

        Apply(loaded, hash, path);
    }

    private void Apply(ProjectLoadResult loaded, string hash, string path)
    {
        if (!_projects.TryApplyExternalContent(loaded.Project, hash, ReloadDescription, out var changed))
        {
            _infoBar.Show(
                InfoBarSeverity.Error,
                $"\"{Path.GetFileName(path)}\" was changed outside DMX Pro, but applying it failed; the project here was " +
                "not changed. Details are in the log.");
            return;
        }

        _autoSaveWarnedHash = null;
        var problems = loaded.Issues.Where(i => i.Severity != ValidationSeverity.Info).ToList();
        if (problems.Count > 0)
        {
            _infoBar.Show(
                problems.Any(i => i.Severity == ValidationSeverity.Error) ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
                $"Reloaded \"{Path.GetFileName(path)}\" (changed outside DMX Pro). {problems.Count} issue(s): " +
                string.Join(" • ", problems.Take(5).Select(i => i.Message)) + (problems.Count > 5 ? " …" : string.Empty));
        }
        else if (changed)
        {
            _infoBar.Show(
                InfoBarSeverity.Success,
                $"Reloaded \"{Path.GetFileName(path)}\": it was changed outside DMX Pro. Ctrl+Z undoes this.");
        }
    }

    private bool IsStillCurrent(Project project, string path, string? knownHash) =>
        ReferenceEquals(project, _projects.Current) &&
        string.Equals(_projects.FilePath, path, StringComparison.OrdinalIgnoreCase) &&
        _projects.DiskContentHash == knownHash;

    private bool HasUnsavedDraft()
    {
        foreach (var source in _drafts)
        {
            if (source.HasUnsavedDraft)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A file event: (re)start the quiet period, then check.</summary>
    private void OnFileEvent()
    {
        if (!_stopped)
        {
            ScheduleCheck(DebounceDelay);
        }
    }

    private void ScheduleCheck(TimeSpan due)
    {
        CancelDebounce();
        if (_stopped)
        {
            return;
        }

        var id = ++_debounceId;
        _debounce = _time.CreateTimer(_ => Post(() => OnCheckDue(id)), null, due, Timeout.InfiniteTimeSpan);
    }

    private void OnCheckDue(long id)
    {
        if (_stopped || id != _debounceId)
        {
            return;
        }

        CancelDebounce();
        RequestCheck();
    }

    private void CancelDebounce()
    {
        _debounce?.Dispose();
        _debounce = null;
        _debounceId++;
    }

    // ───────────── Watcher ─────────────

    private void RearmWatcher()
    {
        var path = !_stopped && Settings.ReloadProjectOnDiskChange ? _projects.FilePath : null;
        if (path is not null)
        {
            path = Path.GetFullPath(path);
        }

        if (string.Equals(path, _watchedPath, StringComparison.OrdinalIgnoreCase) && (_watcher is not null || path is null))
        {
            return;
        }

        DisposeWatcher();
        if (path is null)
        {
            return;
        }

        try
        {
            _watcher = WatcherFactory(path, () => Post(OnFileEvent));
            _watchedPath = path;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _logger.LogWarning("Cannot watch {Path} for outside changes: {Error}", path, ex.Message);
            _infoBar.Show(
                InfoBarSeverity.Warning,
                $"Changes made to \"{Path.GetFileName(path)}\" by other programs cannot be detected in this folder " +
                $"({ex.Message}). Reopen the project to see them.");
        }
    }

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        _watchedPath = null;
    }

    /// <summary>
    /// Watches the folder for the project file: written in place (Changed), re-created (Created) or swapped in by a
    /// rename (Renamed: many tools write a temp file and rename it over the original). A buffer overflow (Error) checks too.
    /// </summary>
    private static IDisposable CreateFileWatcher(string path, Action onChange)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("The project path has no folder.", nameof(path));
        var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };

        void OnEvent(object sender, FileSystemEventArgs e)
        {
            if (string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                onChange();
            }
        }

        watcher.Changed += OnEvent;
        watcher.Created += OnEvent;
        watcher.Renamed += (sender, e) => OnEvent(sender, e);
        watcher.Error += (_, _) => onChange();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    // ───────────── Events ─────────────

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        _unreadableHash = null;
        _autoSaveWarnedHash = null;
        CancelDebounce();
        RearmWatcher();
    }

    // Save As / first save changes the path.
    private void OnProjectStateChanged(object? sender, EventArgs e) => RearmWatcher();

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_stopped)
        {
            return;
        }

        if (_appliedReload != Settings.ReloadProjectOnDiskChange)
        {
            _appliedReload = Settings.ReloadProjectOnDiskChange;
            if (!_appliedReload)
            {
                CancelDebounce();
            }

            RearmWatcher();
        }

        if (_appliedAutoSaveEnabled != Settings.AutoSaveEnabled || _appliedInterval != Settings.AutosaveIntervalMinutes)
        {
            _appliedAutoSaveEnabled = Settings.AutoSaveEnabled;
            _appliedInterval = Settings.AutosaveIntervalMinutes;
            RestartAutoSaveTimer();
        }
    }

    // ───────────── Helpers ─────────────

    /// <summary>Reads the file; null when it does not exist. Retries while its writer still holds it locked.</summary>
    private async Task<byte[]?> ReadFileAsync(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < ReadAttempts)
            {
                await DelayAsync();
            }
        }
    }

    private Task DelayAsync() => RetryDelay > TimeSpan.Zero ? Task.Delay(RetryDelay, _time) : Task.CompletedTask;

    private void Post(Action action)
    {
        if (_ui is null)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }

    private void Resume()
    {
        if (_suspended > 0)
        {
            _suspended--;
        }
    }

    private sealed class Suspension(ProjectFileSyncService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Resume();
            }
        }
    }
}
