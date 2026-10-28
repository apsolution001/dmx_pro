namespace DmxPro.Core.History;

/// <summary>What <see cref="ExternalChangeMerge"/> decided: the content to restore and the file changes held back.</summary>
public sealed class ExternalMergeResult
{
    internal ExternalMergeResult(ProjectSnapshot target, IReadOnlyList<ProjectItemKey> deferred, int discarded)
    {
        Target = target;
        Deferred = deferred;
        Discarded = discarded;
    }

    /// <summary>The content to put into the project (<see cref="ProjectSnapshot.RestoreInto"/>).</summary>
    public ProjectSnapshot Target { get; }

    /// <summary>
    /// Items whose file version is not in <see cref="Target"/> because they are protected (changed or deleted in the file,
    /// or new pages while the console is frozen). They wait for <see cref="ExternalChangeMerge.PlanRelease"/>.
    /// </summary>
    public IReadOnlyList<ProjectItemKey> Deferred { get; }

    /// <summary>
    /// Waiting items that were edited in the app since they were held back: the app's version stays and their file
    /// version is dropped (the next save overwrites the file, like "Keep mine").
    /// </summary>
    public int Discarded { get; }
}
