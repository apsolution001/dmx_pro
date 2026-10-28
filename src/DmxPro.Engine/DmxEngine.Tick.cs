using DmxPro.Core.Patching;
using DmxPro.Engine.Runtime;
using Microsoft.Extensions.Logging;

namespace DmxPro.Engine;

/// <summary>Engine thread side: command application and the per-frame pipeline. Hot path: no allocations, no LINQ.</summary>
public sealed partial class DmxEngine
{
    private double _now;
    private long _seq;
    private long _tickCount;
    private double _sinceSnapshot;
    private bool _snapshotDue = true;
    private int _ticking;
    private bool _showsMuted;
    private StrobeLimit _frameStrobeLimit;

    // Highest release generation (see ReleaseGeneration) among applied commands that remove the preview layer.
    private long _appliedPreviewReleaseGeneration;

    // Live mode as applied on the engine thread (in command order); _liveRequested is what callers last asked for.
    private bool _live;

    /// <summary>
    /// Applies queued commands, advances time by <paramref name="delta"/> (negative/NaN = 0), renders all layers, merges
    /// and clamps every universe, hands the frames to the sinks and (throttled) publishes a snapshot.
    /// Must not be called concurrently.
    /// </summary>
    public void Tick(TimeSpan delta)
    {
        if (Interlocked.Exchange(ref _ticking, 1) != 0)
        {
            throw new InvalidOperationException("DmxEngine.Tick must not be called concurrently.");
        }

        try
        {
            var dt = delta.TotalSeconds;
            if (!(dt > 0) || !double.IsFinite(dt))
            {
                dt = 0;
            }

            ApplyCommands();
            var model = _model;
            _now += dt;
            AdvanceFunctions(dt);
            AdvanceShows(dt);
            Render(model);
            Merge(model);
            Interlocked.Increment(ref _tickCount);
            PublishFrames(model);
            PublishSnapshot(model, dt);
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private void ApplyCommands()
    {
        var commands = _queue.Drain();
        for (var i = 0; i < commands.Count; i++)
        {
            Apply(commands[i]);
        }
    }

    private void Apply(in EngineCommand command)
    {
        switch (command.Kind)
        {
            case EngineCommandKind.StartFunction:
                if (command.Layer == EngineLayer.Preview && _live)
                {
                    _logger.LogDebug("Preview of {FunctionId} ignored: live mode is on", command.Id);
                    break;
                }

                ApplyStart(command.Id, command.Layer, command.Value);
                break;

            case EngineCommandKind.SetPreviewDraft:
                ApplyPreviewDraft(command);
                break;

            case EngineCommandKind.ClearPreview:
                RemovePreviewNow();
                break;

            case EngineCommandKind.SetLiveMode:
                if (command.Value > 0f)
                {
                    NotePreviewRelease(command.Generation);
                }

                ApplyLiveMode(command.Value > 0f);
                break;

            case EngineCommandKind.StopFunction:
                if (FindRunning(command.Id, command.Layer) is { } stopping)
                {
                    // A draft preview has no fade-out (it came up without fade-in either), as with StopPreview.
                    if (stopping.IsDraft)
                    {
                        stopping.InUse = false;
                        _snapshotDue = true;
                    }
                    else
                    {
                        stopping.Stopping = true;
                    }
                }

                break;

            case EngineCommandKind.SetIntensity:
                if (FindRunning(command.Id, command.Layer) is { } running)
                {
                    running.Intensity = command.Value;
                }

                break;

            case EngineCommandKind.SetSpeed:
                if (FindRunning(command.Id, command.Layer) is { } faster)
                {
                    faster.Speed = command.Value;
                }

                break;

            case EngineCommandKind.StopPreview:
                foreach (var function in _functions)
                {
                    if (function.InUse && function.Layer == EngineLayer.Preview)
                    {
                        // A draft preview has no fade-out (it came up without fade-in either).
                        if (function.IsDraft)
                        {
                            function.InUse = false;
                            _snapshotDue = true;
                        }
                        else
                        {
                            function.Stopping = true;
                        }
                    }
                }

                break;

            case EngineCommandKind.SetOverride:
            case EngineCommandKind.ClearOverride:
                if (_live && command.Kind == EngineCommandKind.SetOverride)
                {
                    // Live mode: only the Virtual Console drives the lights; manual overrides are refused.
                    break;
                }

                var flat = _model.FlatIndex(command.Universe, command.Address);
                if (flat >= 0)
                {
                    _model.Overrides[flat] = command.Kind == EngineCommandKind.SetOverride
                        ? (short)command.Value
                        : EngineModel.NoOverride;
                }

                break;

            case EngineCommandKind.ClearOverrides:
                Array.Fill(_model.Overrides, EngineModel.NoOverride);
                break;

            case EngineCommandKind.ReleaseAll:
                NotePreviewRelease(command.Generation);
                ReleaseAllNow();
                break;

            case EngineCommandKind.StartShow:
                if (_live && command.Origin == ShowOrigin.Editor)
                {
                    _logger.LogInformation("Show {ShowId} not started from the editor: live mode is on", command.Id);
                    break;
                }

                ApplyStartShow(command.Id, command.Seconds, command.Origin);
                break;

            case EngineCommandKind.StopShow:
                if (FindShow(command.Id) is { } stopped)
                {
                    stopped.InUse = false;
                    _snapshotDue = true;
                }

                break;

            case EngineCommandKind.PauseShow:
            case EngineCommandKind.ResumeShow:
                if (FindShow(command.Id) is { } paused)
                {
                    paused.Paused = command.Kind == EngineCommandKind.PauseShow;
                    _snapshotDue = true;
                }

                break;

            case EngineCommandKind.SeekShow:
                if (FindShow(command.Id) is { } seeked)
                {
                    // Rendering is a pure function of the position (item ranges + fade wedges), so a jump needs no
                    // per-item state: the next Render shows exactly what the timeline has at the new position.
                    seeked.PositionSeconds = Math.Clamp(command.Seconds, 0, seeked.Program.LengthSeconds);
                    _snapshotDue = true;
                }

                break;

            case EngineCommandKind.LoadModel:
                NotePreviewRelease(command.Generation);
                ReleaseAllNow();
                _model = command.Model!;
                _snapshotDue = true;
                break;

            case EngineCommandKind.RefreshModel:
                Rebind(command.Model!);
                break;
        }
    }

    private void ApplyStart(Guid functionId, EngineLayer layer, float intensity)
    {
        if (!_model.Functions.TryGetValue(functionId, out var program))
        {
            _logger.LogWarning("Function {FunctionId} cannot be played: not found", functionId);
            return;
        }

        if (layer == EngineLayer.Preview)
        {
            // One preview at a time: the old one goes immediately, without fade.
            // A draft preview is replaced too, even with the same id: this start plays the saved function.
            foreach (var function in _functions)
            {
                if (function.InUse && function.Layer == EngineLayer.Preview && (function.FunctionId != functionId || function.IsDraft))
                {
                    function.InUse = false;
                }
            }
        }

        var instance = FindRunning(functionId, layer);
        if (instance is null)
        {
            instance = FindFree();
            if (instance is null)
            {
                _logger.LogWarning("Too many running functions ({Max}); start of {FunctionId} ignored", MaxRunningFunctions, functionId);
                return;
            }

            instance.InUse = true;
            instance.Program = program;
            instance.FunctionId = functionId;
            instance.Layer = layer;
            instance.Level = 0f;
            instance.ElapsedSeconds = 0;
            instance.Speed = 1f;
            instance.StrobeLimit = StrobeLimit.None;
            instance.IsDraft = false;
        }

        instance.Stopping = false;
        instance.Intensity = intensity;
        instance.Stamp = _now;
        instance.Seq = ++_seq;
    }

    /// <summary>
    /// Shows a compiled editor draft in the preview layer. The same draft (by id) keeps its instance: only the program is
    /// swapped, so time and level continue. Anything else in the preview layer goes at once.
    /// </summary>
    private void ApplyPreviewDraft(in EngineCommand command)
    {
        if (_live)
        {
            _logger.LogDebug("Preview of draft {FunctionId} ignored: live mode is on", command.Id);
            return;
        }

        // A release that removes the preview (Release All, project open, live on) was requested after this draft's call
        // started but applied before it (queued while the draft compiled): showing the draft now would undo the release.
        if (command.Generation < _appliedPreviewReleaseGeneration)
        {
            _logger.LogDebug("Preview of draft {FunctionId} dropped: a release was requested while it compiled", command.Id);
            return;
        }

        // Compiled against another patch (a project open, universe change or re-addressing raced it): its flat indexes
        // would land on the wrong channels. The app re-sends the draft after every refresh.
        if (command.Program is not { } program || command.Model is not { } compiledFor || !compiledFor.HasSamePatch(_model))
        {
            return;
        }

        RunningFunction? instance = null;
        foreach (var function in _functions)
        {
            if (!function.InUse || function.Layer != EngineLayer.Preview)
            {
                continue;
            }

            if (function.IsDraft && function.FunctionId == command.Id && instance is null)
            {
                instance = function;
            }
            else
            {
                function.InUse = false;
            }
        }

        if (instance is not null && !instance.Stopping)
        {
            instance.Program = program;
            _snapshotDue = true;
            return;
        }

        instance ??= FindFree();
        if (instance is null)
        {
            _logger.LogWarning("Too many running functions ({Max}); preview of {FunctionId} ignored", MaxRunningFunctions, command.Id);
            return;
        }

        // Draft previews come up at once (no fade-in): the user is editing and wants to see the result now.
        instance.InUse = true;
        instance.Program = program;
        instance.FunctionId = command.Id;
        instance.Layer = EngineLayer.Preview;
        instance.Level = 1f;
        instance.ElapsedSeconds = 0;
        instance.Speed = 1f;
        instance.StrobeLimit = StrobeLimit.None;
        instance.IsDraft = true;
        instance.Stopping = false;
        instance.Intensity = 1f;
        instance.Stamp = _now;
        instance.Seq = ++_seq;
        _snapshotDue = true;
    }

    /// <summary>
    /// Records that a command removing the preview layer was applied. Commands from several threads may be queued out of
    /// generation order, so the highest one wins.
    /// </summary>
    private void NotePreviewRelease(long generation)
    {
        if (generation > _appliedPreviewReleaseGeneration)
        {
            _appliedPreviewReleaseGeneration = generation;
        }
    }

    /// <summary>Removes everything in the preview layer at once, without fade.</summary>
    private void RemovePreviewNow()
    {
        foreach (var function in _functions)
        {
            if (function.InUse && function.Layer == EngineLayer.Preview)
            {
                function.InUse = false;
                _snapshotDue = true;
            }
        }
    }

    /// <summary>
    /// Live mode on: drop the preview and manual overrides and stop editor shows, deterministically at this tick. Off:
    /// nothing that runs changes; previews, overrides and editor shows are simply accepted again.
    /// </summary>
    private void ApplyLiveMode(bool live)
    {
        _live = live;
        if (!live)
        {
            return;
        }

        RemovePreviewNow();
        Array.Fill(_model.Overrides, EngineModel.NoOverride);
        foreach (var show in _shows)
        {
            if (show.InUse && show.Origin == ShowOrigin.Editor)
            {
                show.InUse = false;
                show.Paused = false;
            }
        }

        _snapshotDue = true;
    }

    private void ApplyStartShow(Guid showId, double startSeconds, ShowOrigin origin)
    {
        if (!_model.Shows.TryGetValue(showId, out var program))
        {
            _logger.LogWarning("Show {ShowId} cannot be played: not found", showId);
            return;
        }

        ShowPlayer? player = null;
        foreach (var show in _shows)
        {
            if (show.InUse && show.Program.Id == showId)
            {
                player = show;
                break;
            }

            if (!show.InUse && player is null)
            {
                player = show;
            }
        }

        if (player is null)
        {
            _logger.LogWarning("Too many playing shows ({Max}); start of {ShowId} ignored", MaxPlayingShows, showId);
            return;
        }

        player.InUse = true;
        player.Program = program;
        player.PositionSeconds = startSeconds;
        player.Paused = false;
        player.Origin = origin;
        player.Seq = ++_seq;
        _snapshotDue = true;
    }

    private ShowPlayer? FindShow(Guid showId)
    {
        foreach (var show in _shows)
        {
            if (show.InUse && show.Program.Id == showId)
            {
                return show;
            }
        }

        return null;
    }

    private void ReleaseAllNow()
    {
        foreach (var function in _functions)
        {
            function.InUse = false;
        }

        foreach (var show in _shows)
        {
            show.InUse = false;
            show.Paused = false;
        }

        Array.Fill(_model.Overrides, EngineModel.NoOverride);
        _snapshotDue = true;
    }

    /// <summary>Switches to a recompiled model, keeping what still exists (by id).</summary>
    private void Rebind(EngineModel model)
    {
        foreach (var function in _functions)
        {
            if (function.InUse)
            {
                if (function.IsDraft)
                {
                    // Not part of any model: keep it while its flat indexes stay valid, i.e. the patch did not change
                    // (the app re-sends the draft compiled against the new patch right after the refresh).
                    if (!_model.HasSamePatch(model))
                    {
                        function.InUse = false;
                    }
                }
                else if (model.Functions.TryGetValue(function.FunctionId, out var program))
                {
                    function.Program = program;
                }
                else
                {
                    function.InUse = false;
                }
            }
        }

        foreach (var show in _shows)
        {
            if (show.InUse)
            {
                if (model.Shows.TryGetValue(show.Program.Id, out var program))
                {
                    show.Program = program;
                }
                else
                {
                    show.InUse = false;
                }
            }
        }

        var old = _model;
        for (var slot = 0; slot < old.Universes.Length; slot++)
        {
            var newSlot = model.SlotOf(old.Universes[slot]);
            if (newSlot >= 0)
            {
                old.Overrides.AsSpan(slot * DmxAddress.UniverseSize, DmxAddress.UniverseSize)
                    .CopyTo(model.Overrides.AsSpan(newSlot * DmxAddress.UniverseSize));
            }
        }

        _model = model;
        _snapshotDue = true;
    }

    private RunningFunction? FindRunning(Guid functionId, EngineLayer layer)
    {
        foreach (var function in _functions)
        {
            if (function.InUse && function.FunctionId == functionId && function.Layer == layer)
            {
                return function;
            }
        }

        return null;
    }

    private RunningFunction? FindFree()
    {
        foreach (var function in _functions)
        {
            if (!function.InUse)
            {
                return function;
            }
        }

        return null;
    }

    private void AdvanceFunctions(double dt)
    {
        // The strobe settings decide how long a speed-limited single-shot chaser plays (safety rule 8).
        var context = _context;
        context.AllowStrobe = _allowStrobe;
        context.MaxStrobeRateHz = MaxStrobeRateHz;
        foreach (var function in _functions)
        {
            if (function.InUse && !function.Advance(dt, context))
            {
                function.InUse = false;
            }
        }
    }

    private void AdvanceShows(double dt)
    {
        foreach (var show in _shows)
        {
            if (!show.InUse || show.Paused)
            {
                continue;
            }

            show.PositionSeconds += dt;
            if (show.PositionSeconds >= show.Program.LengthSeconds)
            {
                show.InUse = false;
                _snapshotDue = true;
            }
        }
    }

    private void Render(EngineModel model)
    {
        var layers = model.Layers;
        for (var i = 0; i < layers.Length; i++)
        {
            layers[i].Clear();
        }

        // Mute mode: any console function contributing (incl. one fading out) silences every show on every channel.
        // Shows still advance in AdvanceShows, so they come back at their current position once the console is clear.
        var muted = ShowOverrideMode == ShowOverrideMode.MuteWholeShow && IsConsoleActive();
        if (muted != _showsMuted)
        {
            _showsMuted = muted;
            _snapshotDue = true;
        }

        var context = _context;
        context.AllowStrobe = _allowStrobe;
        context.MaxStrobeRateHz = MaxStrobeRateHz;
        var frameLimit = StrobeLimit.None;

        var showLayer = layers[(int)EngineLayer.Show];
        foreach (var show in _shows)
        {
            if (!show.InUse || muted)
            {
                continue;
            }

            var position = show.PositionSeconds;
            var items = show.Program.Items;

            // Items are sorted by start: skip the ones that already ended, stop at the first one not started yet.
            for (var i = show.Program.FirstLiveIndex(position); i < items.Length; i++)
            {
                var item = items[i];
                if (item.Start > position)
                {
                    break;
                }

                var level = item.LevelAt(position);
                if (level > 0f)
                {
                    var sinceStart = position - item.Start;
                    context.Speed = 1;
                    context.StrobeLimit = StrobeLimit.None;
                    item.Function.Render(sinceStart, level, showLayer, _now - sinceStart, show.Seq, context);
                    frameLimit |= context.StrobeLimit;
                }
            }
        }

        var live = _live;
        foreach (var function in _functions)
        {
            // Live mode gate: nothing reaches the preview layer (it is also emptied when live mode turns on).
            if (!function.InUse || (live && function.Layer == EngineLayer.Preview))
            {
                continue;
            }

            var weight = function.Level * function.Intensity;
            function.StrobeLimit = StrobeLimit.None;
            if (weight > 0f)
            {
                context.Speed = function.Speed;
                context.StrobeLimit = StrobeLimit.None;
                function.Program.Render(function.ElapsedSeconds, weight, layers[(int)function.Layer], function.Stamp, function.Seq, context);
                function.StrobeLimit = context.StrobeLimit;
                frameLimit |= context.StrobeLimit;
            }
        }

        _frameStrobeLimit = frameLimit;
    }

    private bool IsConsoleActive()
    {
        foreach (var function in _functions)
        {
            if (function.InUse && function.Layer == EngineLayer.Console && function.Intensity > 0f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Per channel: profile default, then each layer from low to high (intensity: cross-fade from what is below by the
    /// layer's weight, adding its HTP value; others: the layer's LTP value replaces what is below), then the manual
    /// override, Grand Master and Blackout on intensity, strobe safety, and finally round + clamp to 0–255. The value
    /// after the override (before the masters and strobe safety) is also stored, rounded + clamped, as the requested
    /// value: one extra store per channel into a preallocated buffer.
    /// </summary>
    private void Merge(EngineModel model)
    {
        var grandMaster = (float)GrandMaster;
        var blackout = _blackout;
        var allowStrobe = _allowStrobe;
        var defaults = model.Defaults;
        var isIntensity = model.IsIntensity;
        var isStrobe = model.IsStrobe;
        var strobeSafe = model.StrobeSafe;
        var overrides = model.Overrides;
        var layers = model.Layers;

        for (var slot = 0; slot < model.Output.Length; slot++)
        {
            var output = model.Output[slot];
            var requested = model.Requested[slot];
            var first = slot * DmxAddress.UniverseSize;
            for (var i = 0; i < output.Length; i++)
            {
                var channel = first + i;
                var intensity = isIntensity[channel];
                float value = defaults[channel];

                for (var l = 0; l < layers.Length; l++)
                {
                    var layer = layers[l];
                    var weight = layer.Weight[channel];
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    value = intensity
                        ? (value * (1f - Math.Min(weight, 1f))) + layer.Htp[channel]
                        : layer.Ltp[channel];
                }

                var manual = overrides[channel];
                if (manual >= 0)
                {
                    value = manual;
                }

                requested[i] = ToByte(value);

                if (intensity)
                {
                    value = blackout ? 0f : value * grandMaster;
                }

                if (!allowStrobe && isStrobe[channel])
                {
                    value = strobeSafe[channel];
                }

                output[i] = ToByte(value);
            }
        }
    }

    /// <summary>Rounds half away from zero and clamps to 0–255 (safety rule 9). NaN becomes 0.</summary>
    internal static byte ToByte(float value)
    {
        if (!(value > 0f))
        {
            return 0;
        }

        return value >= 255f ? (byte)255 : (byte)MathF.Round(value, MidpointRounding.AwayFromZero);
    }

    private void PublishFrames(EngineModel model)
    {
        var sinks = Volatile.Read(ref _sinks);
        for (var s = 0; s < sinks.Length; s++)
        {
            for (var slot = 0; slot < model.Output.Length; slot++)
            {
                try
                {
                    sinks[s].OnFrame(model.Universes[slot], model.Output[slot]);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Frame sink {Sink} failed for universe {Universe}", sinks[s].GetType().Name, model.Universes[slot]);
                }
            }
        }
    }

    private void PublishSnapshot(EngineModel model, double dt)
    {
        _sinceSnapshot += dt;
        if (!_snapshotDue && _sinceSnapshot < Volatile.Read(ref _snapshotIntervalSeconds))
        {
            return;
        }

        _snapshotDue = false;
        _sinceSnapshot = 0;

        var frame = _snapshots.Back;
        frame.Sequence = _tickCount;
        frame.TimeSeconds = _now;
        frame.Blackout = _blackout;
        frame.GrandMaster = GrandMaster;
        frame.StrobeLimit = _frameStrobeLimit;
        frame.CopyUniverses(model.Universes, model.Output, model.Requested);

        var count = 0;
        foreach (var function in _functions)
        {
            if (function.InUse)
            {
                frame.Functions[count++] = new ActiveFunctionInfo(
                    function.FunctionId,
                    function.Layer,
                    function.Level,
                    function.Intensity,
                    function.Stopping,
                    function.Speed,
                    function.StrobeLimit);
            }
        }

        frame.FunctionCount = count;

        count = 0;
        foreach (var show in _shows)
        {
            if (show.InUse)
            {
                frame.Shows[count++] = new ShowPlaybackInfo(
                    show.Program.Id,
                    TimeSpan.FromSeconds(show.PositionSeconds),
                    TimeSpan.FromSeconds(show.Program.LengthSeconds),
                    show.Paused,
                    _showsMuted);
            }
        }

        frame.ShowCount = count;
        _snapshots.Publish();
    }
}
