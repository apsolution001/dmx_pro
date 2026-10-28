using DmxPro.Core.Projects;

namespace DmxPro.Core.History;

/// <summary>
/// Undo/redo for one open project, built on whole-project snapshots (<see cref="ProjectSnapshot"/>).
/// <para>
/// Every edit reports itself with <see cref="NoteChange"/> (the app does this in <c>ProjectService.MarkChanged</c>). The
/// snapshot is taken once the edits pause for <see cref="QuietPeriod"/> (<see cref="Capture"/>), so a burst of edits —
/// a dragged fader, a multi-step command — becomes one undo step and dragging does not serialize the project per pixel.
/// Undo, Redo and Save first capture a pending edit, so nothing is lost.
/// </para>
/// <para>
/// Changes made without <see cref="NoteChange"/> (e.g. music paths relinked by a save, an automatic stage placement)
/// are folded into the current state instead of becoming a step of their own.
/// </para>
/// Not thread-safe: use it on the thread that edits the project.
/// </summary>
public sealed class ProjectHistory
{
    /// <summary>Edits closer together than this form one undo step.</summary>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(500);

    private readonly UndoHistory<ProjectSnapshot> _history;
    private bool _pending;
    private TimeSpan _lastChangeAt;

    /// <summary>Creates a history; call <see cref="Reset"/> with the open project before use.</summary>
    /// <param name="maxSteps">Undo steps kept.</param>
    /// <param name="quietPeriod">Pause after which pending edits become a step (default <see cref="DefaultQuietPeriod"/>).</param>
    public ProjectHistory(int maxSteps = UndoHistory<ProjectSnapshot>.DefaultMaxSteps, TimeSpan? quietPeriod = null)
    {
        _history = new UndoHistory<ProjectSnapshot>(maxSteps);
        QuietPeriod = quietPeriod ?? DefaultQuietPeriod;
    }

    public TimeSpan QuietPeriod { get; }

    /// <summary>An edit was reported that is not captured as a step yet.</summary>
    public bool HasPendingChange => _pending;

    /// <summary>True when there is something to undo (a pending edit counts).</summary>
    public bool CanUndo => _pending || _history.CanUndo;

    /// <summary>True when there is something to redo. A pending edit will drop the redo steps, so then false.</summary>
    public bool CanRedo => !_pending && _history.CanRedo;

    /// <summary>What Undo would undo; null for a pending edit not described yet or when there is nothing to undo.</summary>
    public string? UndoDescription => _pending ? null : _history.UndoDescription;

    /// <summary>What Redo would redo, or null.</summary>
    public string? RedoDescription => CanRedo ? _history.RedoDescription : null;

    /// <summary>Undo steps available (a pending edit counts as one).</summary>
    public int UndoCount => _history.UndoCount + (_pending ? 1 : 0);

    /// <summary>Redo steps available.</summary>
    public int RedoCount => CanRedo ? _history.RedoCount : 0;

    /// <summary>
    /// True when the project matches the state last saved (or opened): no pending edit and the history sits on the saved
    /// state. Undoing back to the saved state makes this true again.
    /// </summary>
    public bool IsAtSavedState => !_pending && _history.IsAtSavedState;

    /// <summary>The snapshot the history currently sits on (null before <see cref="Reset"/>).</summary>
    public ProjectSnapshot? Current => _history.Current;

    /// <summary>A different project was opened or created: forget every step and start from its content.</summary>
    /// <param name="project">The new project.</param>
    /// <param name="isSaved">Whether the project counts as saved (true for open and new).</param>
    public void Reset(Project project, bool isSaved = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        _pending = false;
        _history.Reset(ProjectSnapshot.Capture(project), isSaved);
    }

    /// <summary>The project was edited at <paramref name="now"/> (monotonic time).</summary>
    public void NoteChange(TimeSpan now)
    {
        _pending = true;
        _lastChangeAt = now;
    }

    /// <summary>How long until a pending edit should be captured (zero = now); <see cref="TimeSpan.MaxValue"/> when none is pending.</summary>
    public TimeSpan TimeUntilCaptureDue(TimeSpan now)
    {
        if (!_pending)
        {
            return TimeSpan.MaxValue;
        }

        var due = _lastChangeAt + QuietPeriod - now;
        return due > TimeSpan.Zero ? due : TimeSpan.Zero;
    }

    /// <summary>
    /// Captures the project now. A pending edit becomes one undo step (none when the content did not actually change);
    /// without one, unreported differences are folded into the current state. Returns the fresh snapshot, whose items line
    /// up with the project's lists.
    /// </summary>
    public ProjectSnapshot Capture(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var top = _history.Current ?? throw new InvalidOperationException("The history has no state yet; call Reset first.");
        var fresh = ProjectSnapshot.Capture(project, top);
        if (!fresh.ContentEquals(top))
        {
            if (_pending)
            {
                _history.Commit(fresh, ProjectChangeDescriber.Describe(top, fresh));
            }
            else
            {
                _history.ReplaceCurrent(fresh);
            }
        }

        _pending = false;
        return fresh;
    }

    /// <summary>
    /// Undoes the last step (capturing a pending edit first) and puts the previous content back into
    /// <paramref name="project"/> in place. Returns the description of the undone step, or null when there was nothing
    /// to undo. When a snapshot cannot be read back it throws and both the project and the position stay as they were.
    /// </summary>
    public string? Undo(Project project)
    {
        var current = Capture(project);
        var description = _history.UndoDescription;
        var target = _history.Undo();
        if (target is null)
        {
            return null;
        }

        try
        {
            target.RestoreInto(project, current);
        }
        catch
        {
            // The project is untouched (RestoreInto applies nothing when it fails): stay on the state it holds.
            _history.Redo();
            throw;
        }

        return description;
    }

    /// <summary>Redoes the next step and puts its content into <paramref name="project"/>. Returns its description, or null.</summary>
    public string? Redo(Project project)
    {
        var current = Capture(project);
        var description = _history.RedoDescription;
        var target = _history.Redo();
        if (target is null)
        {
            return null;
        }

        try
        {
            target.RestoreInto(project, current);
        }
        catch
        {
            // The project is untouched (RestoreInto applies nothing when it fails): stay on the state it holds.
            _history.Undo();
            throw;
        }

        return description;
    }

    /// <summary>
    /// Puts the content of <paramref name="loaded"/> (the project file as another program changed it) into
    /// <paramref name="project"/> in place, the same way <see cref="Undo"/> does: unchanged items stay the same objects.
    /// A pending edit is captured first, so the in-app edits stay one undo step behind it. The applied content becomes one
    /// undo step named <paramref name="description"/> and is marked saved (it is what the file holds). Returns false when
    /// the file held exactly the current content (nothing applied, no step; the state is marked saved). When the content
    /// cannot be applied it throws and the project and the history stay as they were.
    /// </summary>
    public bool ApplyExternal(Project project, Project loaded, string description)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        return ApplyExternal(project, current => ProjectSnapshot.Capture(loaded, current), description, markSaved: true);
    }

    /// <summary>
    /// Like <see cref="ApplyExternal(Project, Project, string)"/>, for content built from the project's current snapshot
    /// (<paramref name="buildTarget"/> gets it right after a pending edit was captured; e.g. a file change applied only in
    /// part while live, <see cref="ExternalChangeMerge"/>). <paramref name="markSaved"/>: the result counts as saved
    /// (false keeps unsaved in-app edits unsaved). Returns false when the target equals the current content.
    /// </summary>
    public bool ApplyExternal(Project project, Func<ProjectSnapshot, ProjectSnapshot> buildTarget, string description, bool markSaved)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(buildTarget);
        var current = Capture(project);
        var target = buildTarget(current);
        if (target.ContentEquals(current))
        {
            if (markSaved)
            {
                _history.MarkSaved();
            }

            return false;
        }

        // Applies nothing when it throws, so the history is only changed after it succeeded.
        target.RestoreInto(project, current);
        _history.Commit(target, description);
        if (markSaved)
        {
            _history.MarkSaved();
        }

        return true;
    }

    /// <summary>
    /// Call right before writing the file: captures the project (a pending edit becomes a step) and returns a token for
    /// <see cref="MarkSaved"/>.
    /// </summary>
    public ProjectSnapshot PrepareSave(Project project)
    {
        Capture(project);
        return _history.Current!;
    }

    /// <summary>The file now holds <paramref name="saved"/> (from <see cref="PrepareSave"/>).</summary>
    /// <returns>True when the history still sits on that state (no edit happened while saving).</returns>
    public bool MarkSaved(ProjectSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (!ReferenceEquals(_history.Current, saved))
        {
            return false;
        }

        _history.MarkSaved();
        return true;
    }
}
