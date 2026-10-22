namespace DmxPro.Core.History;

/// <summary>
/// A linear undo/redo history of whole states: <c>S0 → S1 → … → Sn</c> with a current position. Undo moves back one
/// state, Redo forward; committing a new state after an undo drops the redo branch. At most <see cref="MaxSteps"/> steps
/// are kept (the oldest state is dropped). The history also remembers which state was last saved, so undoing back to
/// it can show the project as saved again.
/// </summary>
/// <typeparam name="TState">Immutable state (e.g. a <see cref="ProjectSnapshot"/>).</typeparam>
public sealed class UndoHistory<TState>
    where TState : class
{
    /// <summary>Steps kept unless the constructor says otherwise.</summary>
    public const int DefaultMaxSteps = 100;

    private readonly List<TState> _states = [];

    // _descriptions[i] describes the step from state i-1 to state i (index 0 is unused).
    private readonly List<string> _descriptions = [];
    private int _index = -1;
    private int _savedIndex = -1;

    /// <summary>Creates an empty history; call <see cref="Reset"/> before use.</summary>
    /// <param name="maxSteps">Undo steps kept (at least 1).</param>
    public UndoHistory(int maxSteps = DefaultMaxSteps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSteps, 1);
        MaxSteps = maxSteps;
    }

    /// <summary>Most undo steps kept; older ones are dropped.</summary>
    public int MaxSteps { get; }

    /// <summary>The state at the current position (null before <see cref="Reset"/>).</summary>
    public TState? Current => _index >= 0 ? _states[_index] : null;

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index >= 0 && _index < _states.Count - 1;

    /// <summary>Steps that can be undone.</summary>
    public int UndoCount => Math.Max(_index, 0);

    /// <summary>Steps that can be redone.</summary>
    public int RedoCount => _index < 0 ? 0 : _states.Count - 1 - _index;

    /// <summary>What <see cref="Undo"/> would undo, or null.</summary>
    public string? UndoDescription => CanUndo ? _descriptions[_index] : null;

    /// <summary>What <see cref="Redo"/> would redo, or null.</summary>
    public string? RedoDescription => CanRedo ? _descriptions[_index + 1] : null;

    /// <summary>True when the current state is the one last marked saved (and it is still in the history).</summary>
    public bool IsAtSavedState => _index >= 0 && _index == _savedIndex;

    /// <summary>Forgets everything and starts from <paramref name="initial"/>.</summary>
    /// <param name="initial">The first state.</param>
    /// <param name="isSaved">Whether <paramref name="initial"/> counts as saved (a freshly opened or new project).</param>
    public void Reset(TState initial, bool isSaved = true)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _states.Clear();
        _descriptions.Clear();
        _states.Add(initial);
        _descriptions.Add(string.Empty);
        _index = 0;
        _savedIndex = isSaved ? 0 : -1;
    }

    /// <summary>
    /// Adds a new state after the current one as one undo step. Redo steps are dropped; when more than
    /// <see cref="MaxSteps"/> steps exist the oldest state goes.
    /// </summary>
    public void Commit(TState state, string description)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureStarted();
        var redo = _states.Count - 1 - _index;
        if (redo > 0)
        {
            _states.RemoveRange(_index + 1, redo);
            _descriptions.RemoveRange(_index + 1, redo);
            if (_savedIndex > _index)
            {
                // The saved state was on the dropped redo branch: it can never be reached again.
                _savedIndex = -1;
            }
        }

        _states.Add(state);
        _descriptions.Add(string.IsNullOrWhiteSpace(description) ? "Change" : description);
        _index++;

        while (_states.Count - 1 > MaxSteps)
        {
            _states.RemoveAt(0);
            _descriptions.RemoveAt(0);
            _index--;
            _savedIndex = _savedIndex > 0 ? _savedIndex - 1 : -1;
        }
    }

    /// <summary>
    /// Replaces the current state without adding a step (changes that are not an edit of their own, e.g. a save that
    /// relinked music paths). Keeps the saved marker where it is.
    /// </summary>
    public void ReplaceCurrent(TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureStarted();
        _states[_index] = state;
    }

    /// <summary>Moves back one step and returns the state to restore, or null when there is nothing to undo.</summary>
    public TState? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        _index--;
        return _states[_index];
    }

    /// <summary>Moves forward one step and returns the state to restore, or null when there is nothing to redo.</summary>
    public TState? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        _index++;
        return _states[_index];
    }

    /// <summary>The current state is what was just saved.</summary>
    public void MarkSaved()
    {
        EnsureStarted();
        _savedIndex = _index;
    }

    private void EnsureStarted()
    {
        if (_index < 0)
        {
            throw new InvalidOperationException("The history has no state yet; call Reset first.");
        }
    }
}
