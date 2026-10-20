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
                ApplyStart(command.Id, command.Layer, command.Value);
                break;

            case EngineCommandKind.StopFunction:
                if (FindRunning(command.Id, command.Layer) is { } stopping)
                {
                    stopping.Stopping = true;
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
                        function.Stopping = true;
                    }
                }

                break;

            case EngineCommandKind.SetOverride:
            case EngineCommandKind.ClearOverride:
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
                ReleaseAllNow();
                break;

            case EngineCommandKind.StartShow:
                ApplyStartShow(command.Id, command.Seconds);
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
            foreach (var function in _functions)
            {
                if (function.InUse && function.Layer == EngineLayer.Preview && function.FunctionId != functionId)
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
        }

        instance.Stopping = false;
        instance.Intensity = intensity;
        instance.Stamp = _now;
        instance.Seq = ++_seq;
    }

    private void ApplyStartShow(Guid showId, double startSeconds)
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
                if (model.Functions.TryGetValue(function.FunctionId, out var program))
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
        foreach (var function in _functions)
        {
            if (function.InUse && !function.Advance(dt))
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

        foreach (var function in _functions)
        {
            if (!function.InUse)
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
