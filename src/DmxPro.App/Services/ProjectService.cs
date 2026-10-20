using DmxPro.Core.Projects;
using DmxPro.Core.Shows;
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
    public event EventHandler<ProjectContentChangedEventArgs>? ContentChanged;

    public Project Current { get; private set; } = new();

    public string? FilePath { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>
    /// Increases with every content change and project replacement (each <see cref="ContentChanged"/>), so a page can
    /// tell cheaply whether anything changed since it last rebuilt its lists.
    /// </summary>
    public long Revision { get; private set; }

    /// <summary>Call after every edit: marks the project unsaved and notifies listeners.</summary>
    public void MarkChanged() => MarkChanged(affectsPlayback: true);

    /// <summary>
    /// Call after every edit: marks the project unsaved and notifies listeners. Pass
    /// <paramref name="affectsPlayback"/> = false only for edits the engine never reads (names, groups, Virtual Console
    /// layout), so it skips recompiling the project; when in doubt use <see cref="MarkChanged()"/>.
    /// </summary>
    public void MarkChanged(bool affectsPlayback)
    {
        if (!IsDirty)
        {
            IsDirty = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        Revision++;
        ContentChanged?.Invoke(this, affectsPlayback ? ProjectContentChangedEventArgs.Playback : ProjectContentChangedEventArgs.NotPlayback);
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
        // Linked music keeps a path relative to the project file. Every save (Save As, first save, plain save) finds
        // each song from the current location first and relinks it for the target, so songs found only via the old
        // relative path keep working after Save As.
        ShowAudioPaths.UpdateRelativePaths(Current.Shows, FilePath, path, File.Exists);
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
        Revision++;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, ProjectContentChangedEventArgs.Playback);
    }
}
