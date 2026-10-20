using DmxPro.Core.Patching;
using DmxPro.Core.Validation;
using DmxPro.Output.Configuration;

namespace DmxPro.Output;

/// <summary>
/// Owns the configured outputs and the runtime "output enabled" state. Enabling is never persisted: a new manager (app
/// start / project open) is always disabled (safety rule 1). Disabling stops transmission, which is not blackout
/// (safety rule 3).
/// </summary>
/// <remarks>
/// <see cref="Submit"/> is called from the engine thread and is allocation-free; the other members are for the UI /
/// app lifetime and must not be called concurrently with each other. <see cref="StatusChanged"/> fires on background
/// threads.
/// </remarks>
public sealed class OutputManager : IAsyncDisposable
{
    /// <summary>How long app close waits for the safe frame to go out before stopping anyway.</summary>
    public static readonly TimeSpan DefaultSafeFrameTimeout = TimeSpan.FromSeconds(1);

    private readonly IDmxOutputFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusGate = new();
    private List<OutputConfig> _configurations = [];
    private volatile IDmxOutput[] _active = [];
    private volatile bool _closing;
    private OutputStatus _status = OutputStatus.Disabled;

    public OutputManager()
        : this(new DmxOutputFactory())
    {
    }

    public OutputManager(IDmxOutputFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Aggregate status changed (on a background thread).</summary>
    public event EventHandler<OutputStatus>? StatusChanged;

    public IReadOnlyList<OutputConfig> Configurations => _configurations;

    /// <summary>Running outputs (empty while disabled).</summary>
    public IReadOnlyList<IDmxOutput> ActiveOutputs => _active;

    public bool IsEnabled => _active.Length > 0;

    /// <summary>
    /// Disabled while not enabled; otherwise the worst state of all outputs (Error &gt; Connecting &gt; Sending), with
    /// the failing output's name in the message.
    /// </summary>
    public OutputStatus Status
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    /// <summary>Checks a set of configurations; any error blocks <see cref="EnableAsync"/> (safety rule 2).</summary>
    public static IReadOnlyList<ValidationIssue> Validate(IReadOnlyList<OutputConfig> configurations)
    {
        var issues = new List<ValidationIssue>();
        if (configurations.Count == 0)
        {
            issues.Add(ValidationIssue.Error("OUTPUT_NONE_CONFIGURED", "No output is configured. Add an output in Input/Output first."));
            return issues;
        }

        var devices = new Dictionary<string, OutputConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configurations)
        {
            issues.AddRange(config.Validate());
            if (config.DeviceKey is { } key)
            {
                if (devices.TryGetValue(key, out var other))
                {
                    issues.Add(ValidationIssue.Error(
                        "OUTPUT_DEVICE_DUPLICATE",
                        $"{config.DisplayName} and {other.DisplayName} use the same device.",
                        config.Id));
                }
                else
                {
                    devices[key] = config;
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Replaces the configurations. If output is enabled it is disabled first; the user enables it again with the new
    /// settings (no surprise transmission on a changed target).
    /// </summary>
    public async Task SetConfigurationsAsync(IEnumerable<OutputConfig> configurations)
    {
        var list = configurations.ToList();
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DisableCoreAsync().ConfigureAwait(false);
            _configurations = list;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Validates the configurations and starts every output. Returns the issues; if any is an error nothing is started.
    /// Outputs whose device is missing stay enabled in Error state and keep retrying.
    /// </summary>
    public async Task<IReadOnlyList<ValidationIssue>> EnableAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_closing)
            {
                return [ValidationIssue.Error("OUTPUT_CLOSING", "Output is shutting down.")];
            }

            var issues = Validate(_configurations);
            if (_active.Length > 0 || HasError(issues))
            {
                return issues;
            }

            var outputs = new IDmxOutput[_configurations.Count];
            var starts = new Task[outputs.Length];
            for (var i = 0; i < outputs.Length; i++)
            {
                outputs[i] = _factory.Create(_configurations[i]);
                outputs[i].StatusChanged += OnOutputStatusChanged;
                starts[i] = outputs[i].StartAsync(_configurations[i], cancellationToken);
            }

            _active = outputs;
            await Task.WhenAll(starts).ConfigureAwait(false);
            UpdateStatus();
            return issues;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops transmission on all outputs (output disabled — not blackout).</summary>
    public async Task DisableAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await DisableCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Hands a universe frame to every running output (engine thread; allocation-free).</summary>
    public void Submit(int projectUniverse, ReadOnlySpan<byte> frame)
    {
        if (_closing)
        {
            return;
        }

        var outputs = _active;
        for (var i = 0; i < outputs.Length; i++)
        {
            outputs[i].Submit(projectUniverse, frame);
        }
    }

    /// <summary>Safety rule 6 with the same frame (e.g. all zeros = blackout) for every universe.</summary>
    public Task SendSafeFrameAndStopAsync(ReadOnlyMemory<byte> safeFrame, TimeSpan? timeout = null) =>
        SendSafeFrameAndStopAsync(_ => safeFrame, timeout);

    /// <summary>
    /// App close (safety rule 6): ignores further engine frames, sends one safe frame per universe, waits until it has
    /// been transmitted (or <paramref name="timeout"/>), then stops all outputs. The caller stops functions first.
    /// </summary>
    /// <param name="safeFrameForUniverse">Safe frame for a project universe (blackout or profile defaults).</param>
    /// <param name="timeout">Max wait for the frame to go out; default <see cref="DefaultSafeFrameTimeout"/>.</param>
    public async Task SendSafeFrameAndStopAsync(Func<int, ReadOnlyMemory<byte>> safeFrameForUniverse, TimeSpan? timeout = null)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _closing = true;
            var outputs = _active;
            if (outputs.Length == 0)
            {
                return;
            }

            var flushes = new Task<bool>[outputs.Length];
            for (var i = 0; i < outputs.Length; i++)
            {
                foreach (var universe in outputs[i].Universes)
                {
                    var frame = safeFrameForUniverse(universe);
                    outputs[i].Submit(universe, frame.Length > 0 ? frame.Span : new byte[DmxAddress.UniverseSize]);
                }

                flushes[i] = outputs[i].FlushAsync();
            }

            try
            {
                await Task.WhenAll(flushes).WaitAsync(timeout ?? DefaultSafeFrameTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Stop anyway: closing must not hang on a dead device.
            }

            await DisableCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisableAsync().ConfigureAwait(false);
    }

    private static bool HasError(IReadOnlyList<ValidationIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Severity == ValidationSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private async Task DisableCoreAsync()
    {
        var outputs = _active;
        _active = [];
        if (outputs.Length > 0)
        {
            var stops = new Task[outputs.Length];
            for (var i = 0; i < outputs.Length; i++)
            {
                stops[i] = outputs[i].DisposeAsync().AsTask();
            }

            await Task.WhenAll(stops).ConfigureAwait(false);
            foreach (var output in outputs)
            {
                output.StatusChanged -= OnOutputStatusChanged;
            }
        }

        UpdateStatus();
    }

    private void OnOutputStatusChanged(object? sender, OutputStatus status) => UpdateStatus();

    private void UpdateStatus()
    {
        OutputStatus aggregate;
        lock (_statusGate)
        {
            aggregate = ComputeAggregate(_active);
            if (_status == aggregate)
            {
                return;
            }

            _status = aggregate;
        }

        StatusChanged?.Invoke(this, aggregate);
    }

    private static OutputStatus ComputeAggregate(IDmxOutput[] outputs)
    {
        if (outputs.Length == 0)
        {
            return OutputStatus.Disabled;
        }

        string? error = null;
        var connecting = false;
        var fps = 0.0;
        foreach (var output in outputs)
        {
            var status = output.Status;
            switch (status.State)
            {
                case OutputState.Error:
                    error ??= $"{output.Name}: {status.Message}";
                    break;
                case OutputState.Connecting:
                case OutputState.Disabled:
                    connecting = true;
                    break;
                case OutputState.Sending:
                    fps = Math.Max(fps, status.FramesPerSecond);
                    break;
            }
        }

        if (error is not null)
        {
            return OutputStatus.Error(error);
        }

        return connecting ? OutputStatus.Connecting : OutputStatus.Sending(fps);
    }
}
