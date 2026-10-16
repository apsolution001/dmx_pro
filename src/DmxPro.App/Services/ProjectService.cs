using DmxPro.Core.Projects;
using DmxPro.Core.Validation;

namespace DmxPro.App.Services;

/// <summary>Owns the currently open project, its file path, and its unsaved-changes state.</summary>
public sealed class ProjectService
{
    private readonly ProjectSerializer _serializer;

    public ProjectService(ProjectSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>Raised after a different project was created or opened.</summary>
    public event EventHandler? ProjectChanged;

    /// <summary>Raised when the file path or dirty state changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised after any edit to the current project's content (fixtures, groups, …).</summary>
    public event EventHandler? ContentChanged;

    public Project Current { get; private set; } = new();

    public string? FilePath { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>Call after every edit: marks the project unsaved and notifies listeners.</summary>
    public void MarkChanged()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

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
        await _serializer.SaveAsync(Current, path);
        FilePath = path;
        IsDirty = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Replace(Project project, string? filePath)
    {
        Current = project;
        FilePath = filePath;
        IsDirty = false;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
}
