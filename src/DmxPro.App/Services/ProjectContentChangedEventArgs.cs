namespace DmxPro.App.Services;

/// <summary>Details of <see cref="ProjectService.ContentChanged"/>.</summary>
public sealed class ProjectContentChangedEventArgs : EventArgs
{
    /// <summary>An edit that may change what the lights do (the default for every edit).</summary>
    public static readonly ProjectContentChangedEventArgs Playback = new(affectsPlayback: true);

    /// <summary>An edit the engine never reads: names, groups, Virtual Console layout.</summary>
    public static readonly ProjectContentChangedEventArgs NotPlayback = new(affectsPlayback: false);

    private ProjectContentChangedEventArgs(bool affectsPlayback)
    {
        AffectsPlayback = affectsPlayback;
    }

    /// <summary>
    /// False when the edit cannot change playback (fixtures, profiles, functions and shows' timing are untouched), so
    /// the engine does not need to recompile the project. Recompiling a large show project takes long enough to stall
    /// typing a widget label otherwise.
    /// </summary>
    public bool AffectsPlayback { get; }
}
