using DmxPro.Core.Projects;

namespace DmxPro.Core.Functions;

/// <summary>
/// In-app clipboard for whole functions (Edit ▸ Cut / Copy / Paste on the Functions page). It keeps a deep snapshot, so
/// later edits to the source, or deleting it (Cut), do not change what gets pasted. Never saved and independent of the
/// Windows clipboard. Function data references fixtures and other functions by id, so the owner clears it when a
/// different project is opened.
/// </summary>
public sealed class FunctionClipboard
{
    // Element 0 is the copied function, the rest are deep copies of the functions it owns (a chaser's step functions).
    private List<LightFunction>? _snapshot;

    // True when the snapshot holds the owned functions; false after Copy(function), which looks them up at paste time.
    private bool _hasOwned;

    /// <summary>Raised after the content changed (copy or clear).</summary>
    public event EventHandler? Changed;

    /// <summary>True when there is a function to paste.</summary>
    public bool HasFunction => _snapshot is not null;

    /// <summary>Name of the copied function, or null when empty.</summary>
    public string? FunctionName => _snapshot?[0].Name;

    /// <summary>
    /// Stores a deep snapshot of <paramref name="function"/> alone, replacing any earlier content. A chaser's own step
    /// functions are then copied from the project at paste time (only works while the original still exists); prefer
    /// <see cref="Copy(LightFunction, Project)"/>, which snapshots them now.
    /// </summary>
    public void Copy(LightFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        Store([function.CloneAsNew()], hasOwned: false);
    }

    /// <summary>
    /// Stores a deep snapshot of <paramref name="function"/> and of the functions it owns in <paramref name="project"/>
    /// (a chaser's step functions, recursively), replacing any earlier content.
    /// </summary>
    public void Copy(LightFunction function, Project project)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(project);
        Copy(function, project.OwnedFunctions(function.Id));
    }

    /// <summary>
    /// Like <see cref="Copy(LightFunction, Project)"/> with the owned functions given directly, e.g. collected with
    /// <see cref="ChaserSteps.OwnedFunctions"/> before a Cut removed them from the project.
    /// </summary>
    public void Copy(LightFunction function, IEnumerable<LightFunction> owned)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(owned);
        Store(ChaserSteps.CloneWithOwned(function, owned).ToList(), hasOwned: true);
    }

    /// <summary>Empties the clipboard (e.g. when another project is opened).</summary>
    public void Clear()
    {
        if (_snapshot is null)
        {
            return;
        }

        _snapshot = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a new library function built from the snapshot to the end of <paramref name="project"/>'s functions and
    /// returns it, or returns null when the clipboard is empty. Every paste is a separate copy with a new id, named
    /// "&lt;name&gt; (copy)" (then "(copy 2)", …); a copied show copy or step function is pasted as a normal, listed
    /// library function. A chaser's own step functions are pasted as new copies owned by the pasted chaser (added right
    /// after it), so pastes never share step functions.
    /// </summary>
    public LightFunction? PasteInto(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (_snapshot is not { } snapshot)
        {
            return null;
        }

        var root = snapshot[0];
        IReadOnlyList<LightFunction> copies;
        if (_hasOwned)
        {
            copies = ChaserSteps.CloneWithOwned(root, snapshot);

            // The copies point back at the snapshot items; point them at the functions originally copied instead.
            var originalIds = new Dictionary<Guid, Guid?>();
            foreach (var function in snapshot)
            {
                originalIds[function.Id] = function.CopiedFromId;
            }

            foreach (var copy in copies)
            {
                if (copy.CopiedFromId is { } id && originalIds.TryGetValue(id, out var original))
                {
                    copy.CopiedFromId = original;
                }
            }
        }
        else
        {
            // Owned step functions were not snapshotted: copy the original's as they are in the project now.
            var original = root.CloneAsNew();
            original.Id = root.CopiedFromId ?? root.Id;
            copies = ChaserSteps.CloneWithOwned(original, project.OwnedFunctions(original.Id));
            copies[0].CopiedFromId = root.CopiedFromId;
        }

        var pasted = copies[0];
        pasted.OwnerShowId = null;
        pasted.OwnerFunctionId = null;
        pasted.ShowInFunctionList = true;
        pasted.Name = PasteName(project, root.Name);
        project.Functions.AddRange(copies);
        return pasted;
    }

    /// <summary>First free "&lt;name&gt; (copy)", "&lt;name&gt; (copy 2)", … among all function names, case-insensitive.</summary>
    public static string PasteName(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in project.Functions)
        {
            names.Add(function.Name);
        }

        var candidate = $"{name} (copy)";
        for (var n = 2; names.Contains(candidate); n++)
        {
            candidate = $"{name} (copy {n})";
        }

        return candidate;
    }

    private void Store(List<LightFunction> snapshot, bool hasOwned)
    {
        _snapshot = snapshot;
        _hasOwned = hasOwned;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
