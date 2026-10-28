using System.Windows.Threading;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Engine;
using DmxPro.Output;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.Services;

/// <summary>
/// Live edit preview (owner decision 2026-10-22): while a function editor is open on the visible page and live mode is
/// off, its unsaved draft is shown on the lights automatically through the engine preview layer, following every edit.
/// Pages call <see cref="Attach"/> when their editor opens or the page becomes visible and <see cref="Detach"/> when the
/// editor closes or the page is left; there is no Preview button or "Live on lights" switch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Smoothness.</b> Edits are throttled to one recompile per <see cref="ThrottleInterval"/> (trailing): a dragged
/// slider updates the lights ~25 times a second with the latest value. Compiling happens here on the UI thread (the
/// project is edited there); the engine thread only swaps the compiled program in, keeping the effect's time running.
/// </para>
/// <para>
/// <b>Stops</b> (deterministically, without fade; the lights return to whatever else runs, or to defaults): editor
/// closed / saved-and-closed / cancelled, page left, project replaced, live mode on, app close (engine Release All) and
/// Release All. After Release All the preview stays off until the next edit in the still-open editor (a new user action);
/// after live mode goes off it comes back by itself because the editor is still open. Only one preview at a time: the
/// most recently attached editor wins.
/// </para>
/// <para>
/// <b>Safety.</b> It is only a layer: nothing goes out while output is disabled (safety rule 1), Grand Master, Blackout,
/// "allow strobe" and the maximum strobe rate (rule 8) and clamping (rule 9) apply as for every other source. A timed
/// flash previews as held while its editor is open; switching to another program keeps it on, like every other light
/// (owner decision 2026-10-22, safety rule 5). A release from any thread wins over a pending throttled send: every send first
/// checks <see cref="DmxEngine.ReleaseGeneration"/> synchronously (rule 4). From the start of the close sequence
/// nothing is sent any more (rule 6).
/// </para>
/// Call every member on the UI thread.
/// </remarks>
public sealed class FunctionPreviewService
{
    /// <summary>Minimum time between two preview recompiles while the user keeps editing.</summary>
    public static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(40);

    public const string ShowingText = "Showing on lights";
    public const string OutputOffText = "Output off — not on lights";
    public const string LiveText = "Live mode — not on lights";
    public const string ReleasedText = "Released — change something to show it again";
    public const string FailedText = "Preview failed — not on lights";

    private readonly DmxEngine _engine;
    private readonly ProjectService _projects;
    private readonly ILiveModeService _liveMode;
    private readonly OutputManager _output;
    private readonly ILogger<FunctionPreviewService> _logger;
    private readonly SynchronizationContext? _uiContext;
    private FunctionEditorViewModel? _editor;
    private DispatcherTimer? _throttle;
    private bool _released;
    private bool _failed;
    private bool _closed;

    // Engine release generation the current preview belongs to: a newer one means Release All / project open / live
    // mode removed the preview since, so a pending send must not bring it back.
    private long _generation;

    public FunctionPreviewService(
        DmxEngine engine,
        ProjectService projects,
        ILiveModeService liveMode,
        EngineConsolePlayback playback,
        EngineRuntimeService runtime,
        OutputManager output,
        ILogger<FunctionPreviewService> logger)
    {
        _engine = engine;
        _projects = projects;
        _liveMode = liveMode;
        _output = output;
        _logger = logger;
        _uiContext = SynchronizationContext.Current;

        liveMode.LiveModeChanged += (_, _) => OnLiveModeChanged();
        playback.AllReleased += (_, _) => OnUiThread(OnAllReleased);
        runtime.PatchRefreshed += (_, _) => SendNow();
        runtime.ProjectLoaded += (_, _) =>
        {
            if (_editor is { } editor)
            {
                Detach(editor);
            }
        };
        output.StatusChanged += (_, _) => OnUiThread(UpdateStatus);
        runtime.ShuttingDown += (_, _) => OnShuttingDown();
    }

    /// <summary>The editor whose draft is previewed, or null.</summary>
    public FunctionEditorViewModel? Current => _editor;

    /// <summary>
    /// Shows <paramref name="editor"/>'s draft on the lights from now on (replacing any other editor's preview). Calling
    /// it again for the attached editor re-sends the draft.
    /// </summary>
    public void Attach(FunctionEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!ReferenceEquals(_editor, editor))
        {
            if (_editor is { } old)
            {
                Detach(old);
            }

            _editor = editor;
            editor.DraftChanged += OnDraftChanged;
        }

        _released = false;
        _generation = _engine.ReleaseGeneration;
        SendNow();
        UpdateStatus();
    }

    /// <summary>
    /// Stops previewing <paramref name="editor"/> (removed at the next engine tick, without fade). Does nothing when
    /// another editor is attached, so a page that is left late never ends another page's preview.
    /// </summary>
    public void Detach(FunctionEditorViewModel editor)
    {
        if (!ReferenceEquals(_editor, editor))
        {
            return;
        }

        editor.DraftChanged -= OnDraftChanged;
        editor.PreviewStatus = null;
        _editor = null;
        _failed = false;
        _throttle?.Stop();
        _engine.ClearPreview();
    }

    private void OnDraftChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _editor))
        {
            return;
        }

        if (_closed)
        {
            return;
        }

        if (_released)
        {
            // A new user action after a release: show the draft again from the current engine state on.
            _released = false;
            _generation = _engine.ReleaseGeneration;
            UpdateStatus();
        }

        _throttle ??= CreateThrottle();
        if (!_throttle.IsEnabled)
        {
            _throttle.Start();
        }
    }

    private DispatcherTimer CreateThrottle()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = ThrottleInterval };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SendNow();
        };
        return timer;
    }

    /// <summary>Compiles the current draft and hands it to the engine preview layer (or clears it when it cannot play).</summary>
    private void SendNow()
    {
        if (_editor is not { } editor || _closed || _liveMode.IsLive || _engine.IsLiveMode || _released)
        {
            return;
        }

        if (_engine.ReleaseGeneration != _generation)
        {
            // Released since the last send (possibly from another thread, its AllReleased post not handled yet).
            MarkReleased();
            return;
        }

        var failed = false;
        try
        {
            if (!_engine.SetPreviewFunction(_projects.Current, editor.BuildPreviewFunction(), editor.PreviewFunctions))
            {
                _engine.ClearPreview();
            }
        }
        catch (Exception ex)
        {
            // The editor keeps working; only the light preview is missing. Visible in the editor hint.
            _logger.LogError(ex, "Previewing the draft of {Function} failed", editor.Original.Name);
            _engine.ClearPreview();
            failed = true;
        }

        if (failed != _failed)
        {
            _failed = failed;
            UpdateStatus();
        }
    }

    private void OnLiveModeChanged()
    {
        if (_liveMode.IsLive)
        {
            // The engine already dropped the preview when it applied live mode; clear explicitly anyway.
            _throttle?.Stop();
            _engine.ClearPreview();
        }
        else
        {
            // Switching live mode on counted as a release; the still-open editor's preview comes back by itself.
            _generation = _engine.ReleaseGeneration;
            SendNow();
        }

        UpdateStatus();
    }

    private void OnAllReleased()
    {
        // Engine Release All already removed the preview (safety rule 4); stay off until the user edits again. Posted:
        // when the user already edited again after this release (generation adopted), there is nothing to do.
        if (_editor is not null && _engine.ReleaseGeneration != _generation)
        {
            MarkReleased();
        }
    }

    private void OnShuttingDown()
    {
        _closed = true;
        _throttle?.Stop();
    }

    private void MarkReleased()
    {
        // Usually already gone in the engine (Release All / live mode); clearing again is harmless and deterministic.
        _engine.ClearPreview();
        _throttle?.Stop();
        _released = true;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        editor.PreviewStatus = _liveMode.IsLive ? LiveText
            : _released ? ReleasedText
            : _failed ? FailedText
            : !_output.IsEnabled ? OutputOffText
            : ShowingText;
    }

    private void OnUiThread(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }
}
