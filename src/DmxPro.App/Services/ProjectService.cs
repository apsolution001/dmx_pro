using System.Diagnostics;
using DmxPro.Core.History;
using DmxPro.Core.Projects;
using DmxPro.Core.Shows;
using DmxPro.Core.Validation;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.Services;

/// <summary>
/// Owns the currently open project, its file path, its unsaved-changes state and its undo/redo history
/// (<see cref="ProjectHistory"/>: every <see cref="MarkChanged()"/> is an undoable edit).
/// </summary>
public sealed class ProjectService
{
    private readonly ProjectSerializer _serializer;
    private readonly ILogger<ProjectService> _logger;
    private readonly ProjectHistory _history = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _captureScheduled;
    private bool _restoring;

    // False when the history could not start for the open project (logged): undo is then unavailable rather than
    // restoring content of another project.
    private bool _historyEnabled;

    public ProjectService(ProjectSerializer serializer, ILogger<ProjectService> logger)
    {
        _serializer = serializer;
        _logger = logger;
        ResetHistory();
    }

    /// <summary>Raised after a different project was created or opened.</summary>
    public event EventHandler? ProjectChanged;

    /// <summary>Raised when the file path or dirty state changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised after any edit to the current project's content (fixtures, groups, …), undo and redo included.</summary>
    public event EventHandler<ProjectContentChangedEventArgs>? ContentChanged;

    /// <summary>
    /// Raised after Undo / Redo put earlier content back (right after its <see cref="ContentChanged"/>). The
    /// <see cref="Current"/> project and its lists stay the same objects, and so does every item whose content did not
    /// change; changed, removed or re-added items are other objects now. Pages that hold items (rows, editors) rebuild
    /// from the project here. Unlike <see cref="ProjectChanged"/> this is not a project open: output, live mode and
    /// running functions stay as they are.
    /// </summary>
    public event EventHandler? ContentRestored;

    /// <summary>Raised when <see cref="CanUndo"/>, <see cref="CanRedo"/> or their descriptions may have changed.</summary>
    public event EventHandler? HistoryChanged;

    public Project Current { get; private set; } = new();

    public string? FilePath { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>
    /// Increases with every content change, undo/redo and project replacement (each <see cref="ContentChanged"/>), so a
    /// page can tell cheaply whether anything changed since it last rebuilt its lists.
    /// </summary>
    public long Revision { get; private set; }

    public bool CanUndo => _historyEnabled && _history.CanUndo;

    public bool CanRedo => _historyEnabled && _history.CanRedo;

    /// <summary>What Undo would undo ("Delete fixture "Par 1""); null while the last edit is still being typed/dragged.</summary>
    public string? UndoDescription => _historyEnabled ? _history.UndoDescription : null;

    /// <summary>What Redo would redo, or null.</summary>
    public string? RedoDescription => _historyEnabled ? _history.RedoDescription : null;

    /// <summary>Call after every edit: marks the project unsaved and notifies listeners.</summary>
    public void MarkChanged() => MarkChanged(affectsPlayback: true);

    /// <summary>
    /// Call after every edit: marks the project unsaved, records an undo step (edits in quick succession — a dragged
    /// fader — form one step) and notifies listeners. Pass <paramref name="affectsPlayback"/> = false only for edits the
    /// engine never reads (names, groups, Virtual Console layout), so it skips recompiling the project; when in doubt use
    /// <see cref="MarkChanged()"/>.
    /// </summary>
    public void MarkChanged(bool affectsPlayback)
    {
        // A page repairing something while it rebuilds after undo/redo is not a new edit: it is folded into the restored
        // state at the next capture instead of wiping the redo steps.
        if (!_restoring && _historyEnabled)
        {
            var hadPending = _history.HasPendingChange;
            _history.NoteChange(_clock.Elapsed);
            ScheduleCapture();
            if (!hadPending)
            {
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        if (!IsDirty)
        {
            IsDirty = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        Revision++;
        ContentChanged?.Invoke(this, affectsPlayback ? ProjectContentChangedEventArgs.Playback : ProjectContentChangedEventArgs.NotPlayback);
    }

    /// <summary>
    /// Undoes the last edit in place (see <see cref="ContentRestored"/>). <paramref name="description"/> says what was
    /// undone (null when there was nothing to undo). Returns false when it failed (logged; the project is unchanged).
    /// </summary>
    public bool TryUndo(out string? description) => TryRestore(undo: true, out description);

    /// <summary>Redoes the last undone edit in place; see <see cref="TryUndo"/>.</summary>
    public bool TryRedo(out string? description) => TryRestore(undo: false, out description);

    public void New()
    {
        Replace(new Project(), filePath: null);
    }

    public async Task<IReadOnlyList<ValidationIssue>> OpenAsync(string path)
    {
        var result = await _serializer.LoadAsync(path);
        Replace(result.Project, path);
        return result.Issues;
    }

    public async Task SaveAsync(string path)
    {
        // Linked music keeps a path relative to the project file. Every save (Save As, first save, plain save) finds
        // each song from the current location first and relinks it for the target, so songs found only via the old
        // relative path keep working after Save As.
        ShowAudioPaths.UpdateRelativePaths(Current.Shows, FilePath, path, File.Exists);

        // The relinked paths are not an edit of their own: they are folded into the current undo state.
        ProjectSnapshot? saved = null;
        if (_historyEnabled)
        {
            try
            {
                saved = _history.PrepareSave(Current);
            }
            catch (Exception ex)
            {
                // The undo history must never stop a save.
                _logger.LogError(ex, "Recording the saved state for undo failed");
            }
        }

        await _serializer.SaveAsync(Current, path);
        FilePath = path;
        if (saved is not null)
        {
            _history.MarkSaved(saved);
        }

        IsDirty = saved is not null && !_history.IsAtSavedState;
        StateChanged?.Invoke(this, EventArgs.Empty);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryRestore(bool undo, out string? description)
    {
        description = null;
        if (undo ? !CanUndo : !CanRedo)
        {
            return true;
        }

        try
        {
            description = undo ? _history.Undo(Current) : _history.Redo(Current);
        }
        catch (Exception ex)
        {
            // Never crash an edit session: the snapshot is applied only after it was read completely, so the project
            // stays as it was.
            _logger.LogError(ex, "{Action} failed", undo ? "Undo" : "Redo");
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (description is null)
        {
            // Only a pending edit that changed nothing was there.
            UpdateDirtyFromHistory();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var dirty = !_history.IsAtSavedState;
        if (dirty != IsDirty)
        {
            IsDirty = dirty;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        Revision++;
        _restoring = true;
        try
        {
            ContentChanged?.Invoke(this, ProjectContentChangedEventArgs.Playback);
            ContentRestored?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _restoring = false;
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>An edit that changed nothing after all (or was taken back by hand to the saved state) shows as saved.</summary>
    private void UpdateDirtyFromHistory()
    {
        if (IsDirty && _history.IsAtSavedState)
        {
            IsDirty = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResetHistory()
    {
        try
        {
            _history.Reset(Current);
            _historyEnabled = true;
        }
        catch (Exception ex)
        {
            _historyEnabled = false;
            _logger.LogError(ex, "Starting the undo history failed; undo is unavailable for this project");
        }
    }

    private void ScheduleCapture()
    {
        if (_captureScheduled)
        {
            return;
        }

        _captureScheduled = true;
        _ = CaptureWhenQuietAsync();
    }

    /// <summary>
    /// Takes the undo snapshot once the edits pause (one step per burst), on the UI thread where the project is edited.
    /// Undo, Redo and Save capture a still pending edit themselves.
    /// </summary>
    private async Task CaptureWhenQuietAsync()
    {
        try
        {
            while (_historyEnabled && _history.HasPendingChange)
            {
                var wait = _history.TimeUntilCaptureDue(_clock.Elapsed);
                if (wait <= TimeSpan.Zero)
                {
                    _history.Capture(Current);
                    UpdateDirtyFromHistory();
                    HistoryChanged?.Invoke(this, EventArgs.Empty);
                    break;
                }

                // Resumes on the UI thread (captured context).
                await Task.Delay(wait);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording an undo step failed");
        }
        finally
        {
            _captureScheduled = false;
        }
    }

    private void Replace(Project project, string? filePath)
    {
        Current = project;
        FilePath = filePath;
        IsDirty = false;
        ResetHistory();

        Revision++;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, ProjectContentChangedEventArgs.Playback);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
