using DmxPro.Core.History;
using DmxPro.Core.Projects;
using DmxPro.Engine;

namespace DmxPro.App.Services;

/// <summary>
/// Tells what is on the lights right now, for keeping it unchanged while live (<see cref="ProjectFileSyncService"/>):
/// what the Virtual Console commanded (seen at once, before the engine's next tick) plus what the latest engine snapshot
/// reports (functions still fading out, shows). Read on the UI thread; the engine snapshot may be one tick old.
/// </summary>
public sealed class PlayingContentService
{
    private readonly DmxEngine _engine;
    private readonly EngineConsolePlayback _playback;

    public PlayingContentService(DmxEngine engine, EngineConsolePlayback playback)
    {
        _engine = engine;
        _playback = playback;
        _playback.StateChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised (on any thread) when the console started, stopped or changed something.</summary>
    public event EventHandler? Changed;

    /// <summary>What of <paramref name="project"/> must not change now (<see cref="ProtectedContent.FromPlaying"/>).</summary>
    public ProtectedContent GetProtectedContent(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var functions = new HashSet<Guid>();
        var shows = new HashSet<Guid>();
        var flashHeld = _playback.CollectPlaying(functions, shows);
        if (_engine.GetSnapshot() is { } snapshot)
        {
            foreach (var function in snapshot.ActiveFunctions)
            {
                functions.Add(function.FunctionId);
            }

            foreach (var show in snapshot.Shows)
            {
                shows.Add(show.ShowId);
            }
        }

        return ProtectedContent.FromPlaying(project, functions, shows, flashHeld);
    }
}
