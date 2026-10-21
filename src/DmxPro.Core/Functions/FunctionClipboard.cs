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
    private LightFunction? _snapshot;

    /// <summary>Raised after the content changed (copy or clear).</summary>
    public event EventHandler? Changed;

    /// <summary>True when there is a function to paste.</summary>
    public bool HasFunction => _snapshot is not null;

    /// <summary>Name of the copied function, or null when empty.</summary>
    public string? FunctionName => _snapshot?.Name;

    /// <summary>Stores a deep snapshot of <paramref name="function"/>, replacing any earlier content.</summary>
    public void Copy(LightFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var snapshot = function.CloneAsNew();
        snapshot.CopiedFromId = function.Id;
        _snapshot = snapshot;
        Changed?.Invoke(this, EventArgs.Empty);
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
    /// "&lt;name&gt; (copy)" (then "(copy 2)", …); a copied show copy is pasted as a normal, listed library function.
    /// </summary>
    public LightFunction? PasteInto(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (_snapshot is not { } snapshot)
        {
            return null;
        }

        var copy = snapshot.CloneAsNew();
        copy.CopiedFromId = snapshot.CopiedFromId;
        copy.OwnerShowId = null;
        copy.ShowInFunctionList = true;
        copy.Name = PasteName(project, snapshot.Name);
        project.Functions.Add(copy);
        return copy;
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
}
