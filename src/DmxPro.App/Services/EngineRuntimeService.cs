using DmxPro.Engine;
using DmxPro.Output;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace DmxPro.App.Services;

/// <summary>
/// Owns the engine/output lifetime for the app: starts the engine thread, keeps the engine in sync with the open
/// project, and runs the close sequence (safety rule 6). Output is never enabled here — the Input/Output page does
/// that on the user's request (safety rule 1).
/// </summary>
public sealed class EngineRuntimeService
{
    /// <summary>
    /// Shown in the shell info bar (error severity: it stays until closed or the next good refresh, safety rule 10) when
    /// the engine could not take a project edit and keeps playing the previous model — e.g. a deleted chaser still runs.
    /// </summary>
    public const string RefreshFailedMessage = "Engine could not apply the latest edit — press Release All";

    /// <summary>
    /// Shown (error severity, stays until closed or the next good load) when the engine could not load the opened
    /// project: it keeps the previous project, whose lights may still be active. Needs its own text because Release All
    /// alone does not load the new project.
    /// </summary>
    public const string LoadFailedMessage =
        "The lights engine could not load this project — the previous project's lights may still be active. Press Release All, then reopen the project.";

    private readonly ShowMusicService _music;
    private readonly DmxEngine _engine;
    private readonly EngineHost _host;
    private readonly OutputManager _output;
    private readonly OutputFrameSink _sink;
    private readonly ProjectService _projects;
    private readonly EngineConsolePlayback _playback;
    private readonly ILogger<EngineRuntimeService> _logger;
    private readonly InfoBarState _infoBar;
    private readonly object _gate = new();
    private SynchronizationContext? _uiContext;
    private bool _started;
    private bool _refreshPending;
    private Task? _shutdown;
    private volatile int _safeFrameMode = (int)SafeFrameMode.ProfileDefaults;

    public EngineRuntimeService(
        DmxEngine engine,
        EngineHost host,
        OutputManager output,
        OutputFrameSink sink,
        ProjectService projects,
        EngineConsolePlayback playback,
        ShowMusicService music,
        InfoBarState infoBar,
        ILogger<EngineRuntimeService> logger)
    {
        _infoBar = infoBar;
        _music = music;
        _engine = engine;
        _host = host;
        _output = output;
        _sink = sink;
        _projects = projects;
        _playback = playback;
        _logger = logger;
    }

    /// <summary>
    /// Raised on the UI thread right after a project was loaded into the engine (which released everything, manual
    /// overrides included).
    /// </summary>
    public event EventHandler? ProjectLoaded;

    /// <summary>
    /// Raised on the UI thread right after the engine patch was refreshed for a project edit (coalesced: at most once
    /// per dispatcher turn). Handlers may queue engine commands; they apply after the refresh.
    /// </summary>
    public event EventHandler? PatchRefreshed;

    /// <summary>
    /// Raised on the UI thread at the very start of the close sequence, before anything is released. Handlers must stop
    /// queueing engine commands (e.g. the edit preview); the engine refuses previews and overrides from then on anyway.
    /// </summary>
    public event EventHandler? ShuttingDown;

    /// <summary>
    /// True while the last engine refresh after a project edit failed: the engine still plays the model from before that
    /// edit. Cleared by the next successful refresh or project load. UI thread only.
    /// </summary>
    public bool HasRefreshFailure { get; private set; }

    /// <summary>
    /// True while the last project load into the engine failed: the engine still plays the project from before.
    /// Cleared by the next successful load. UI thread only.
    /// </summary>
    public bool HasLoadFailure { get; private set; }

    /// <summary>Frame sent once on close (safety rule 6). Settings sets this; default profile defaults.</summary>
    public SafeFrameMode SafeFrameMode
    {
        get => (SafeFrameMode)_safeFrameMode;
        set => _safeFrameMode = (int)value;
    }

    /// <summary>Loads the current project and starts the engine thread. Call once, on the UI thread, at app start.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        _uiContext = SynchronizationContext.Current;
        _engine.AddFrameSink(_sink);
        LoadCurrentProject();
        _projects.ProjectChanged += OnProjectChanged;
        _projects.ContentChanged += OnContentChanged;
        _host.Start();
    }

    /// <summary>
    /// App close, safety rule 6: stop all functions → send one safe frame (<see cref="SafeFrameMode"/>) → stop output →
    /// stop the engine thread. Idempotent (every call returns the same task); never throws (failures are logged).
    /// </summary>
    public Task ShutdownAsync()
    {
        lock (_gate)
        {
            return _shutdown ??= ShutdownCoreAsync();
        }
    }

    private async Task ShutdownCoreAsync()
    {
        _projects.ProjectChanged -= OnProjectChanged;
        _projects.ContentChanged -= OnContentChanged;
        var mode = SafeFrameMode;
        try
        {
            ShuttingDown?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A shutdown handler failed");
        }

        // 1. Stop functions (applied by the still-running engine at its next tick) and the music, still on the UI
        // thread (before the first await).
        _engine.ReleaseAll();

        // Live mode from now on: the engine refuses edit previews, manual overrides and editor show starts, so a pending
        // preview throttle tick or a Test page slider can not put anything back between Release All and the safe frame.
        // Console functions are already released and nothing restarts them during close.
        _engine.SetLiveMode(true);
        try
        {
            _music.StopAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stopping the show music failed");
        }

        // 2 + 3. One safe frame per universe, wait until it went out, then stop every output. From here on the manager
        // ignores engine frames, so nothing the engine still renders can overwrite the safe frame.
        try
        {
            await _output.SendSafeFrameAndStopAsync(universe => _engine.CreateSafeFrame(universe, mode)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sending the safe frame / stopping output failed");
        }

        // 4. Stop the engine thread (joins it, so off the UI thread).
        try
        {
            await Task.Run(() => _host.Stop(mode)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stopping the engine failed");
        }

        _engine.RemoveFrameSink(_sink);
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        // Safety rule 1: every project open starts with output disabled. Does nothing when it is not enabled.
        _ = DisableOutputAsync();
        LoadCurrentProject();
        _playback.ResetAfterProjectLoad();
        ProjectLoaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Edits can come in bursts (dragging a fader): coalesce them into one recompile per UI dispatcher turn. Edits the
    /// engine never reads (names, groups, console layout) skip the recompile, which is costly for large show projects.
    /// Undo / Redo arrive here too (a content change, never <see cref="ProjectService.ProjectChanged"/>): the refresh
    /// keeps running functions and shows that still exist, stops the ones the undo removed, and leaves output, live
    /// mode, Grand Master, Blackout and manual overrides alone.
    /// </summary>
    private void OnContentChanged(object? sender, ProjectContentChangedEventArgs e)
    {
        if (!e.AffectsPlayback || _refreshPending)
        {
            return;
        }

        _refreshPending = true;
        if (_uiContext is null)
        {
            RefreshNow();
        }
        else
        {
            _uiContext.Post(_ => RefreshNow(), null);
        }
    }

    private void RefreshNow()
    {
        if (!_refreshPending || _shutdown is not null)
        {
            return;
        }

        _refreshPending = false;
        try
        {
            // Compiled on the UI thread on purpose: the project is edited there, so reading it elsewhere would race.
            _engine.RefreshPatch(_projects.Current);
            _playback.SyncWithModel();
        }
        catch (Exception ex)
        {
            // The engine keeps the previous model, so removed functions may keep playing. Safety rules 4 + 10: say so
            // visibly and point at Release All. Never release automatically: the owner may be running a live show.
            _logger.LogError(ex, "Updating the engine after a project edit failed");
            ReportRefreshFailure();
            return;
        }

        ClearRefreshFailure();
        try
        {
            PatchRefreshed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // The engine took the edit; only a listener failed.
            _logger.LogError(ex, "A patch-refreshed handler failed");
        }
    }

    private void ReportRefreshFailure()
    {
        HasRefreshFailure = true;

        // The failed load already showed the more specific message (a project open also triggers a refresh).
        if (HasLoadFailure)
        {
            return;
        }

        // Also re-shows the message when the user closed the bar and a later edit fails again.
        _infoBar.Show(InfoBarSeverity.Error, RefreshFailedMessage, autoHide: false);
    }

    private void ClearRefreshFailure()
    {
        if (!HasRefreshFailure)
        {
            return;
        }

        HasRefreshFailure = false;

        // Only hide our own message: a newer message (a save, an import error) must stay.
        if (_infoBar.Message == RefreshFailedMessage)
        {
            _infoBar.Show(InfoBarSeverity.Informational, null);
        }
    }

    private void LoadCurrentProject()
    {
        _refreshPending = false;
        try
        {
            _engine.LoadProject(_projects.Current);

            // A fresh model replaced the stale one (and released everything): the old refresh failure no longer applies.
            ClearRefreshFailure();
            ClearLoadFailure();
        }
        catch (Exception ex)
        {
            // The engine keeps the previous project. Safety rules 4 + 10: say so visibly; never release automatically.
            _logger.LogError(ex, "Loading the project into the engine failed");
            HasLoadFailure = true;
            _infoBar.Show(InfoBarSeverity.Error, LoadFailedMessage, autoHide: false);
        }
    }

    private void ClearLoadFailure()
    {
        if (!HasLoadFailure)
        {
            return;
        }

        HasLoadFailure = false;
        if (_infoBar.Message == LoadFailedMessage)
        {
            _infoBar.Show(InfoBarSeverity.Informational, null);
        }
    }

    private async Task DisableOutputAsync()
    {
        try
        {
            await _output.DisableAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disabling output on project open failed");
        }
    }
}
