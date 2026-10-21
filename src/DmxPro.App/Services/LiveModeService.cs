using DmxPro.Engine;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.Services;

/// <summary>
/// Engine-backed <see cref="ILiveModeService"/>. The engine gate does the suppressing; this service also stops the
/// Shows-page shows through the playback (so their music stops) and turns live mode off whenever a project is loaded.
/// Call from the UI thread.
/// </summary>
public sealed class LiveModeService : ILiveModeService
{
    private readonly DmxEngine _engine;
    private readonly EngineConsolePlayback _playback;
    private readonly ILogger<LiveModeService> _logger;

    public LiveModeService(DmxEngine engine, EngineConsolePlayback playback, EngineRuntimeService runtime, ILogger<LiveModeService> logger)
    {
        _engine = engine;
        _playback = playback;
        _logger = logger;

        // Live mode always starts off on project open (owner decision 2026-10-22), like output (safety rule 1).
        runtime.ProjectLoaded += (_, _) => SetLive(false);
    }

    public event EventHandler? LiveModeChanged;

    public bool IsLive { get; private set; }

    public void SetLive(bool live)
    {
        if (live == IsLive)
        {
            return;
        }

        IsLive = live;
        _engine.SetLiveMode(live);
        if (live)
        {
            var stopped = _playback.StopEditorShows();
            _logger.LogInformation("Live mode on: only the Virtual Console drives the lights ({Stopped} editor shows stopped)", stopped);
        }
        else
        {
            _logger.LogInformation("Live mode off");
        }

        LiveModeChanged?.Invoke(this, EventArgs.Empty);
    }
}
