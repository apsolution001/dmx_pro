using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Engine.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DmxPro.Engine;

/// <summary>
/// The DMX playback engine. Owns one 512-byte buffer per project universe, runs functions in priority layers and
/// merges them into frames:
/// <c>defaults → Show layer → Console layer → Preview layer → manual overrides → Grand Master → Blackout → strobe
/// safety → clamp</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading.</b> All command methods and properties are thread-safe and only queue work; it is applied at the start
/// of the next <see cref="Tick"/>. <see cref="Tick"/> itself must be called from one thread at a time (normally the
/// <see cref="EngineHost"/> thread); it allocates nothing and holds no lock except for swapping the command queue and
/// the snapshot buffers.
/// </para>
/// <para>
/// <b>Live mode.</b> <see cref="SetLiveMode"/> gates everything that is not the Virtual Console: while on, the preview
/// layer, manual overrides and <see cref="ShowOrigin.Editor"/> shows are dropped and refused.
/// </para>
/// <para>
/// <b>Safety.</b> The engine has no notion of "output enabled": it only computes frames. Enabling transmission is the
/// output layer's job and always starts disabled (safety rule 1). Blackout is a frame state, not output disable.
/// </para>
/// </remarks>
public sealed partial class DmxEngine
{
    /// <summary>Maximum function instances running at once over all layers; further starts are ignored and logged.</summary>
    public const int MaxRunningFunctions = 256;

    /// <summary>Maximum shows playing at once.</summary>
    public const int MaxPlayingShows = 16;

    /// <summary>Highest speed factor accepted by <see cref="SetFunctionSpeed"/>.</summary>
    public const double MaxSpeed = 8;

    /// <summary>Default interval between UI snapshots (~30 Hz).</summary>
    public static readonly TimeSpan DefaultSnapshotInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly ILogger _logger;
    private readonly CommandQueue _queue = new();
    private readonly RunningFunction[] _functions = new RunningFunction[MaxRunningFunctions];
    private readonly ShowPlayer[] _shows = new ShowPlayer[MaxPlayingShows];
    private readonly SnapshotStore _snapshots = new(MaxRunningFunctions, MaxPlayingShows);
    private readonly object _sinkGate = new();
    private readonly RenderContext _context = new();

    private IFrameSink[] _sinks = [];
    private EngineModel _model;
    private volatile EngineModel _latestModel;
    private double _grandMaster = 1.0;
    private volatile bool _blackout;
    private volatile bool _allowStrobe;
    private double _maxStrobeRateHz = 10;
    private double _snapshotIntervalSeconds = DefaultSnapshotInterval.TotalSeconds;
    private volatile int _showOverrideMode = (int)ShowOverrideMode.MuteWholeShow;
    private long _releaseGeneration;
    private volatile bool _liveRequested;

    /// <summary>Creates an engine with an empty project (universe 1, nothing patched).</summary>
    public DmxEngine(ILogger<DmxEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<DmxEngine>.Instance;
        for (var i = 0; i < _functions.Length; i++)
        {
            _functions[i] = new RunningFunction();
        }

        for (var i = 0; i < _shows.Length; i++)
        {
            _shows[i] = new ShowPlayer();
        }

        _model = EngineModelBuilder.Build(new Project(), _logger);
        _latestModel = _model;
    }

    /// <summary>Project universes (from 1) of the most recently loaded project.</summary>
    public IReadOnlyList<int> Universes => Array.AsReadOnly(_latestModel.Universes);

    /// <summary>
    /// Grand Master 0..1. Scales intensity channels only (Dimmer and colors), never strobe/macro/speed. Out-of-range
    /// values are clamped; NaN is treated as 0.
    /// </summary>
    public double GrandMaster
    {
        get => Volatile.Read(ref _grandMaster);
        set => Volatile.Write(ref _grandMaster, Clamp01(value));
    }

    /// <summary>
    /// Blackout: intensity channels are sent as 0 while everything else (and every running function) keeps going.
    /// Not the same as disabling output (safety rule 3).
    /// </summary>
    public bool Blackout
    {
        get => _blackout;
        set => _blackout = value;
    }

    /// <summary>
    /// Global "allow strobe" setting. While false (the default), every Strobe-type channel is forced to its safe value:
    /// the profile default when it lies in a capability marked safe, else the start of the first safe range, else the
    /// default.
    /// </summary>
    public bool AllowStrobe
    {
        get => _allowStrobe;
        set => _allowStrobe = value;
    }

    /// <summary>
    /// Maximum strobe rate in Hz (safety rule 8), enforced on software strobe functions: their real flash rate
    /// (rate × speed) is capped to it and <see cref="StrobeLimit.RateLimited"/> is reported; 0 means no strobing at all
    /// (steady light, <see cref="StrobeLimit.Suppressed"/>). Hardware strobe channels cannot be limited because profiles
    /// carry no value → Hz calibration, so they are governed by <see cref="AllowStrobe"/> only. Values below 0 or NaN are
    /// treated as 0.
    /// </summary>
    public double MaxStrobeRateHz
    {
        get => Volatile.Read(ref _maxStrobeRateHz);
        set => Volatile.Write(ref _maxStrobeRateHz, double.IsFinite(value) && value > 0 ? value : 0);
    }

    /// <summary>
    /// How console functions interact with playing shows. <see cref="ShowOverrideMode.MuteWholeShow"/> (default): while
    /// any console function runs, shows are muted on all channels but keep advancing.
    /// <see cref="ShowOverrideMode.TouchedChannelsOnly"/>: plain layer priority. Preview, manual overrides, Grand Master
    /// and Blackout apply on top in both modes.
    /// </summary>
    public ShowOverrideMode ShowOverrideMode
    {
        get => (ShowOverrideMode)_showOverrideMode;
        set => _showOverrideMode = value is ShowOverrideMode.MuteWholeShow or ShowOverrideMode.TouchedChannelsOnly
            ? (int)value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown show override mode.");
    }

    /// <summary>
    /// Number of completed ticks. A command queued while this reads <c>n</c> is reflected in every snapshot whose
    /// <see cref="EngineSnapshot.Sequence"/> is at least <c>n + 2</c> (a tick may be in progress past its command drain).
    /// </summary>
    public long TickCount => Interlocked.Read(ref _tickCount);

    /// <summary>
    /// Increases every time a command that clears <b>all</b> manual overrides is queued: <see cref="ReleaseAll"/>,
    /// <see cref="ClearOverrides"/> and <see cref="LoadProject"/>. Counted at queue time, just <i>before</i> the command
    /// is queued, because commands apply in queue order: an override recorded with a generation lower than the current
    /// one was queued before that clear and is (or will be) gone. One recorded with the current generation normally
    /// survives (it may not when queued concurrently from another thread; treating it as alive is the safe side, since
    /// its holder will still clear it). Holders of overrides (the Test page session) compare it to forget overrides that
    /// are gone. Never touched by the tick.
    /// </summary>
    public long ReleaseGeneration => Interlocked.Read(ref _releaseGeneration);

    /// <summary>Minimum time between UI snapshots. <see cref="TimeSpan.Zero"/> publishes one every tick.</summary>
    public TimeSpan SnapshotInterval
    {
        get => TimeSpan.FromSeconds(Volatile.Read(ref _snapshotIntervalSeconds));
        set => Volatile.Write(ref _snapshotIntervalSeconds, Math.Max(0, value.TotalSeconds));
    }

    /// <summary>
    /// Opens a project: resolves the patch and compiles functions/shows on the calling thread, then (at the next tick)
    /// releases everything and switches to it. Call with the project not being edited concurrently.
    /// </summary>
    public void LoadProject(Project project)
    {
        var model = EngineModelBuilder.Build(project, _logger);
        _latestModel = model;
        Interlocked.Increment(ref _releaseGeneration);
        _queue.Enqueue(new EngineCommand(EngineCommandKind.LoadModel, Model: model));
    }

    /// <summary>
    /// Re-resolves the patch and recompiles functions/shows after an edit, keeping running functions, shows and
    /// overrides (re-bound by id; ones that no longer exist stop).
    /// </summary>
    public void RefreshPatch(Project project)
    {
        var model = EngineModelBuilder.Build(project, _logger);
        _latestModel = model;
        _queue.Enqueue(new EngineCommand(EngineCommandKind.RefreshModel, Model: model));
    }

    /// <summary>Where a fixture channel is patched, per the most recently loaded project.</summary>
    public bool TryResolveChannel(Guid fixtureId, string channelKey, out PatchedChannel channel) =>
        _latestModel.Patch.TryGetValue((fixtureId, channelKey), out channel);

    /// <summary>True when the function exists in the most recently loaded project and compiled (every function type).</summary>
    public bool CanPlay(Guid functionId) => _latestModel.Functions.ContainsKey(functionId);

    /// <summary>True when the show exists in the most recently loaded project.</summary>
    public bool CanPlayShow(Guid showId) => _latestModel.Shows.ContainsKey(showId);

    /// <summary>
    /// Starts a function in a layer with its own fade-in. Starting one that already runs in that layer updates its
    /// intensity, cancels a fade-out and makes it the latest (LTP). Starting in <see cref="EngineLayer.Preview"/>
    /// behaves like <see cref="StartPreview"/> (replaces the current preview).
    /// </summary>
    /// <param name="functionId">Function to start.</param>
    /// <param name="layer">Layer to play in.</param>
    /// <param name="intensity">Function intensity 0..1 (clamped).</param>
    public void StartFunction(Guid functionId, EngineLayer layer, double intensity = 1.0) =>
        _queue.Enqueue(new EngineCommand(EngineCommandKind.StartFunction, functionId, ValidLayer(layer), (float)Clamp01(intensity)));

    /// <summary>Stops a function in a layer with its fade-out. Does nothing when it is not running there.</summary>
    public void StopFunction(Guid functionId, EngineLayer layer) =>
        _queue.Enqueue(new EngineCommand(EngineCommandKind.StopFunction, functionId, ValidLayer(layer)));

    /// <summary>Changes the intensity (0..1, clamped) of a running function, e.g. from a console slider.</summary>
    public void SetFunctionIntensity(Guid functionId, EngineLayer layer, double intensity) =>
        _queue.Enqueue(new EngineCommand(EngineCommandKind.SetIntensity, functionId, ValidLayer(layer), (float)Clamp01(intensity)));

    /// <summary>
    /// Changes the speed factor of a running function: its own time (chaser steps, colour cycle, pulse, strobe rate)
    /// runs <paramref name="speed"/> times as fast; fades in/out on start/stop stay real time. Clamped to
    /// 0..<see cref="MaxSpeed"/>; NaN is treated as 1. A new start resets the speed to 1. The strobe cap applies to the
    /// sped-up rate.
    /// </summary>
    /// <param name="functionId">Running function.</param>
    /// <param name="layer">Layer it runs in.</param>
    /// <param name="speed">Speed factor (1 = as programmed, 0 = frozen).</param>
    public void SetFunctionSpeed(Guid functionId, EngineLayer layer, double speed) =>
        _queue.Enqueue(new EngineCommand(
            EngineCommandKind.SetSpeed,
            functionId,
            ValidLayer(layer),
            (float)(double.IsNaN(speed) ? 1 : Math.Clamp(speed, 0, MaxSpeed))));

    /// <summary>
    /// Previews a function on top of everything that runs (it wins on the channels it touches and stops nothing).
    /// Only one preview exists: a running preview is removed immediately and replaced. Ignored while live mode is on.
    /// </summary>
    public void StartPreview(Guid functionId) => StartFunction(functionId, EngineLayer.Preview);

    /// <summary>Stops the preview (with its fade-out); the lights return to what runs underneath.</summary>
    public void StopPreview() => _queue.Enqueue(new EngineCommand(EngineCommandKind.StopPreview));

    /// <summary>
    /// Shows an unsaved editor draft in the <see cref="EngineLayer.Preview"/> layer (live edit preview). The draft is
    /// compiled here, on the calling thread, against the patch of the most recently loaded project (functions it
    /// references are taken from <paramref name="project"/> as saved); the tick only swaps the compiled program in, so
    /// call this on every (debounced) draft change. While a draft with the same id is previewed, its program is replaced
    /// in place: elapsed time and level are kept, so effects keep running smoothly while the user edits. Anything else
    /// in the preview layer is replaced. Draft previews start and stop without fades. A timed flash previews as held.
    /// Ignored (and false returned when known here) while live mode is on, or when the type cannot be played. Call with
    /// the project not being edited concurrently (normally the UI thread).
    /// </summary>
    /// <param name="project">The open project (fixtures, profiles, referenced functions).</param>
    /// <param name="draft">The editor's draft; may be a copy that is not in the project.</param>
    /// <returns>True when the preview was queued.</returns>
    public bool SetPreviewFunction(Project project, LightFunction draft)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(draft);
        if (_liveRequested)
        {
            return false;
        }

        var model = _latestModel;
        var program = EngineModelBuilder.CompileDraft(project, draft, model, _logger);
        if (program is null)
        {
            return false;
        }

        _queue.Enqueue(new EngineCommand(EngineCommandKind.SetPreviewDraft, draft.Id, EngineLayer.Preview, 1f, Model: model, Program: program));
        return true;
    }

    /// <summary>
    /// Removes the preview at the next tick immediately, without fade (editor closed, page left, live mode on); the
    /// lights return to what runs underneath. Deterministic counterpart of <see cref="StopPreview"/>.
    /// </summary>
    public void ClearPreview() => _queue.Enqueue(new EngineCommand(EngineCommandKind.ClearPreview));

    /// <summary>
    /// Live mode (owner decision 2026-10-22, like QLC+ Operate mode): only the Virtual Console drives the lights. When
    /// switched on (at the next tick, in queue order), the preview layer is removed without fade, every manual override
    /// is cleared (counts as a release for <see cref="ReleaseGeneration"/>) and every <see cref="ShowOrigin.Editor"/>
    /// show stops. While on, previews, manual overrides and editor show starts are ignored. Console functions,
    /// console shows, Grand Master, Blackout and Release All are not affected, and switching off changes nothing that
    /// runs (the lights do not jump); other sources are simply allowed again. This is not output enable/disable
    /// (safety rules 1–3). Starts off and is never persisted.
    /// </summary>
    public void SetLiveMode(bool live)
    {
        if (live)
        {
            Interlocked.Increment(ref _releaseGeneration);
        }

        _liveRequested = live;
        _queue.Enqueue(new EngineCommand(EngineCommandKind.SetLiveMode, Value: live ? 1f : 0f));
    }

    /// <summary>The live mode last requested with <see cref="SetLiveMode"/> (applies at the next tick).</summary>
    public bool IsLiveMode => _liveRequested;

    /// <summary>
    /// Manual override of one channel (Test tab / simple desk). Wins over all function layers, but Grand Master,
    /// Blackout and strobe safety still apply. Ignored for universes the project does not use, and while live mode is on.
    /// </summary>
    /// <param name="universe">Project universe, from 1.</param>
    /// <param name="address">DMX address 1–512.</param>
    /// <param name="value">Raw value.</param>
    public void SetChannelOverride(int universe, int address, byte value)
    {
        ValidateAddress(universe, address);
        _queue.Enqueue(new EngineCommand(EngineCommandKind.SetOverride, Value: value, Universe: universe, Address: address));
    }

    /// <summary>Removes the override of one channel.</summary>
    public void ClearChannelOverride(int universe, int address)
    {
        ValidateAddress(universe, address);
        _queue.Enqueue(new EngineCommand(EngineCommandKind.ClearOverride, Universe: universe, Address: address));
    }

    /// <summary>Removes all manual overrides.</summary>
    public void ClearOverrides()
    {
        Interlocked.Increment(ref _releaseGeneration);
        _queue.Enqueue(new EngineCommand(EngineCommandKind.ClearOverrides));
    }

    /// <summary>
    /// Release All (safety rule 4): at the next tick every function in every layer (including the preview) and every
    /// show stops immediately, without fades, and all overrides are cleared, so every channel returns to its profile
    /// default. Grand Master and Blackout are settings and are left as they are.
    /// </summary>
    public void ReleaseAll()
    {
        Interlocked.Increment(ref _releaseGeneration);
        _queue.Enqueue(new EngineCommand(EngineCommandKind.ReleaseAll));
    }

    /// <summary>
    /// Plays a show from <paramref name="startPosition"/> (restarts it when already playing). Each item plays its
    /// function in the <see cref="EngineLayer.Show"/> layer between its start and end, with the item's fade wedges.
    /// The show stops by itself at its end.
    /// </summary>
    /// <param name="showId">Show to play.</param>
    /// <param name="startPosition">Where to start.</param>
    /// <param name="origin">
    /// Where it was started from; an <see cref="ShowOrigin.Editor"/> start is ignored while live mode is on (see
    /// <see cref="SetLiveMode"/>).
    /// </param>
    public void StartShow(Guid showId, TimeSpan startPosition = default, ShowOrigin origin = ShowOrigin.Console) =>
        _queue.Enqueue(new EngineCommand(
            EngineCommandKind.StartShow,
            showId,
            Seconds: Math.Max(0, startPosition.TotalSeconds),
            Origin: origin == ShowOrigin.Editor ? ShowOrigin.Editor : ShowOrigin.Console));

    /// <summary>Stops a show immediately (playing or paused); its items release.</summary>
    public void StopShow(Guid showId) => _queue.Enqueue(new EngineCommand(EngineCommandKind.StopShow, showId));

    /// <summary>
    /// Pauses a playing show: its position freezes and its lights hold their current look. Does nothing when the show
    /// is not playing.
    /// </summary>
    public void PauseShow(Guid showId) => _queue.Enqueue(new EngineCommand(EngineCommandKind.PauseShow, showId));

    /// <summary>Continues a paused show from its frozen position. Does nothing when the show is not playing.</summary>
    public void ResumeShow(Guid showId) => _queue.Enqueue(new EngineCommand(EngineCommandKind.ResumeShow, showId));

    /// <summary>
    /// Jumps a playing or paused show to <paramref name="position"/> (clamped to 0..length), keeping its playing/paused
    /// state. Items whose range contains the new position render with their correct fade level at once. Seeking to the
    /// end of a playing show stops it at the next tick. Does nothing when the show is not playing.
    /// </summary>
    public void SeekShow(Guid showId, TimeSpan position) =>
        _queue.Enqueue(new EngineCommand(EngineCommandKind.SeekShow, showId, Seconds: Math.Max(0, position.TotalSeconds)));

    /// <summary>Position of a playing show from the latest snapshot, or null when it is not playing.</summary>
    public TimeSpan? GetShowPosition(Guid showId) => TryGetShowPlayback(showId, out var info, out _) ? info.Position : null;

    /// <summary>
    /// Reads one show's state from the latest snapshot without copying the universes (cheap enough for a 30 Hz UI
    /// timer). <paramref name="snapshotSequence"/> is the snapshot's tick sequence, or -1 before the first snapshot.
    /// </summary>
    /// <returns>True when the show was playing (or paused) in the latest snapshot.</returns>
    public bool TryGetShowPlayback(Guid showId, out ShowPlaybackInfo info, out long snapshotSequence) =>
        _snapshots.TryGetShow(showId, out info, out snapshotSequence);

    /// <summary>
    /// Builds the frame to send once on close (safety rule 6) from the most recently loaded project: every channel at
    /// its profile default, strobe channels at their safe value, and with <see cref="SafeFrameMode.Blackout"/> all
    /// intensity channels at 0. Returns 512 zeros for universes the project does not use. Allocates (not for the tick).
    /// </summary>
    /// <param name="universe">Project universe, from 1.</param>
    /// <param name="mode">Profile defaults or blackout.</param>
    public byte[] CreateSafeFrame(int universe, SafeFrameMode mode)
    {
        var frame = new byte[DmxAddress.UniverseSize];
        var model = _latestModel;
        var slot = model.SlotOf(universe);
        if (slot < 0)
        {
            return frame;
        }

        var first = slot * DmxAddress.UniverseSize;
        for (var i = 0; i < frame.Length; i++)
        {
            var channel = first + i;
            frame[i] = model.IsStrobe[channel] ? model.StrobeSafe[channel]
                : mode == SafeFrameMode.Blackout && model.IsIntensity[channel] ? (byte)0
                : model.Defaults[channel];
        }

        return frame;
    }

    /// <summary>A copy of the latest published state, or null before the first tick. Allocates on the caller's thread.</summary>
    public EngineSnapshot? GetSnapshot() => _snapshots.Read();

    /// <summary>Registers a sink that receives every frame (e.g. an output). Adding the same sink twice is ignored.</summary>
    public void AddFrameSink(IFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_sinkGate)
        {
            if (Array.IndexOf(_sinks, sink) < 0)
            {
                _sinks = [.. _sinks, sink];
            }
        }
    }

    /// <summary>Unregisters a frame sink.</summary>
    public void RemoveFrameSink(IFrameSink sink)
    {
        lock (_sinkGate)
        {
            var index = Array.IndexOf(_sinks, sink);
            if (index >= 0)
            {
                var copy = new List<IFrameSink>(_sinks);
                copy.RemoveAt(index);
                _sinks = [.. copy];
            }
        }
    }

    private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    private static EngineLayer ValidLayer(EngineLayer layer) =>
        layer is EngineLayer.Show or EngineLayer.Console or EngineLayer.Preview
            ? layer
            : throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown engine layer.");

    private static void ValidateAddress(int universe, int address)
    {
        if (!DmxAddress.IsValidUniverse(universe))
        {
            throw new ArgumentOutOfRangeException(nameof(universe), universe, "Universe must be 1 or higher.");
        }

        if (!DmxAddress.IsValid(address))
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "DMX address must be 1–512.");
        }
    }
}
