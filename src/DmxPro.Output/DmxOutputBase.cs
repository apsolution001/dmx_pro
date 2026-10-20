using System.Diagnostics;
using DmxPro.Core.Patching;
using DmxPro.Core.Validation;
using DmxPro.Output.Configuration;
using DmxPro.Output.Transport;

namespace DmxPro.Output;

/// <summary>
/// Shared machinery for outputs: pre-allocated per-universe buffers, a dedicated send thread with a fixed refresh rate
/// (plus an early send when data changes, never faster than <see cref="OutputConfig.MaxFrameRateHz"/>), automatic
/// reconnect, fps measurement and status events. Subclasses only open/close their transport and send one universe.
/// </summary>
/// <remarks>
/// Threading: <see cref="Prepare"/> runs on the caller of <see cref="StartAsync"/> before the send thread exists;
/// <see cref="OpenTransport"/>, <see cref="SendUniverse"/>, <see cref="BeforeClose"/> and <see cref="CloseTransport"/>
/// run only on the send thread. Exceptions from them are turned into an Error status and never reach the engine.
/// </remarks>
public abstract class DmxOutputBase<TConfig> : IDmxOutput
    where TConfig : OutputConfig
{
    /// <summary>Delay between reconnect attempts after a failure (USB unplugged, network down…).</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _statusGate = new();
    private readonly object _flushGate = new();
    private List<TaskCompletionSource<bool>> _pendingFlushes = [];
    private List<TaskCompletionSource<bool>> _cycleFlushes = [];
    private bool _acceptingFlushes;

    private volatile UniverseSlot[] _slots = [];
    private int[] _universes = [];
    private Thread? _thread;
    private TaskCompletionSource? _threadExited;
    private TaskCompletionSource? _firstOpenAttempt;
    private volatile bool _stopRequested;
    private OutputStatus _status = OutputStatus.Disabled;
    private string _name;
    private int _frameRateHz = OutputConfig.DefaultFrameRateHz;
    private bool _disposed;

    protected DmxOutputBase(string defaultName)
    {
        _name = defaultName;
    }

    public event EventHandler<OutputStatus>? StatusChanged;

    public string Name => _name;

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

    public IReadOnlyList<int> Universes => _universes;

    /// <summary>Reconnect delay; shortened by tests.</summary>
    internal TimeSpan RetryDelay { get; set; } = RetryInterval;

    /// <summary>The configuration in use since the last <see cref="StartAsync"/>.</summary>
    protected TConfig? Config { get; private set; }

    public async Task StartAsync(OutputConfig config, CancellationToken cancellationToken = default)
    {
        Task firstAttempt;
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);

            if (config is not TConfig typed)
            {
                SetStatus(OutputStatus.Error($"{Name}: unsupported configuration ({config?.Kind.ToString() ?? "none"})."));
                return;
            }

            _name = typed.DisplayName;
            var firstError = FirstError(typed.Validate());
            if (firstError is not null)
            {
                // Safety rule 2: never transmit with an invalid configuration.
                SetStatus(OutputStatus.Error($"Invalid configuration — {firstError.Message}"));
                return;
            }

            var universes = typed.GetValidUniverses();
            try
            {
                Prepare(typed, universes);
            }
            catch (Exception e)
            {
                SetStatus(OutputStatus.Error(DescribeError(e)));
                return;
            }

            var slots = new UniverseSlot[universes.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = new UniverseSlot(universes[i]);
            }

            Config = typed;
            _frameRateHz = typed.FrameRateHz;
            _universes = universes;
            _slots = slots;
            _stopRequested = false;
            _firstOpenAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _threadExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_flushGate)
            {
                _acceptingFlushes = true;
            }

            SetStatus(OutputStatus.Connecting);
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = $"DMX output: {_name}",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
            firstAttempt = _firstOpenAttempt.Task;
        }
        finally
        {
            _lifecycle.Release();
        }

        try
        {
            await firstAttempt.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller stopped waiting; the output keeps connecting in the background.
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public void Submit(int projectUniverse, ReadOnlySpan<byte> frame)
    {
        var slots = _slots;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot.Universe != projectUniverse)
            {
                continue;
            }

            var length = Math.Min(frame.Length, DmxAddress.UniverseSize);
            var source = frame[..length];
            bool changed;
            lock (slot)
            {
                changed = !slot.HasData || slot.Length != length || !source.SequenceEqual(slot.Pending.AsSpan(0, length));
                if (changed)
                {
                    source.CopyTo(slot.Pending);
                    slot.Length = length;
                    slot.HasData = true;
                }
            }

            if (changed)
            {
                _wake.Set();
            }

            return;
        }
    }

    public Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> flush;
        lock (_flushGate)
        {
            if (!_acceptingFlushes)
            {
                return Task.FromResult(false);
            }

            flush = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingFlushes.Add(flush);
        }

        _wake.Set();
        return cancellationToken.CanBeCanceled ? flush.Task.WaitAsync(cancellationToken) : flush.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!_disposed)
            {
                _disposed = true;
                DisposeResources();
                if (_thread is null)
                {
                    _wake.Dispose();
                }
            }
        }
        finally
        {
            _lifecycle.Release();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Validates mapping and pre-allocates per-universe state. Throwing here leaves the output in Error.</summary>
    /// <param name="config">The validated configuration.</param>
    /// <param name="universes">Project universes in slot order (slot index = position).</param>
    protected abstract void Prepare(TConfig config, IReadOnlyList<int> universes);

    /// <summary>Opens the socket / device. Throw (preferably <see cref="OutputTransportException"/>) on failure.</summary>
    protected abstract void OpenTransport();

    /// <summary>Transmits one universe. Hot path: must not allocate.</summary>
    /// <param name="slotIndex">Index into the universes passed to <see cref="Prepare"/>.</param>
    /// <param name="data">Slot values (up to 512, start code excluded).</param>
    protected abstract void SendUniverse(int slotIndex, ReadOnlySpan<byte> data);

    /// <summary>Closes the transport. Must not throw.</summary>
    protected abstract void CloseTransport();

    /// <summary>Last chance to send while the transport is still open (e.g. sACN stream-terminated packets).</summary>
    protected virtual void BeforeClose()
    {
    }

    /// <summary>Releases transport objects owned by the subclass on dispose.</summary>
    protected virtual void DisposeResources()
    {
    }

    /// <summary>Turns an exception into the human-readable status message.</summary>
    protected virtual string DescribeError(Exception exception) => ErrorDescriptions.Describe(exception);

    /// <summary>The last data handed to <see cref="SendUniverse"/> for <paramref name="slotIndex"/> (send thread only).</summary>
    protected bool TryGetLastSent(int slotIndex, out ReadOnlySpan<byte> data)
    {
        var slots = _slots;
        if ((uint)slotIndex < (uint)slots.Length && slots[slotIndex].SentLength > 0)
        {
            data = slots[slotIndex].Sending.AsSpan(0, slots[slotIndex].SentLength);
            return true;
        }

        data = default;
        return false;
    }

    private static ValidationIssue? FirstError(IReadOnlyList<ValidationIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Severity == ValidationSeverity.Error)
            {
                return issue;
            }
        }

        return null;
    }

    private static int TicksToWaitMilliseconds(long ticks) =>
        (int)Math.Clamp((ticks * 1000 + Stopwatch.Frequency - 1) / Stopwatch.Frequency, 1, int.MaxValue);

    private static void CompleteFlushes(List<TaskCompletionSource<bool>> flushes, bool result)
    {
        foreach (var flush in flushes)
        {
            flush.TrySetResult(result);
        }

        flushes.Clear();
    }

    private async Task StopCoreAsync()
    {
        var thread = _thread;
        var exited = _threadExited;
        if (thread is null || exited is null)
        {
            SetStatus(OutputStatus.Disabled);
            return;
        }

        _stopRequested = true;
        _wake.Set();
        try
        {
            await exited.Task.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The transport is stuck in a driver call; the background thread exits once it returns.
            SetStatus(new OutputStatus(OutputState.Disabled, $"{Name} did not stop cleanly (device not responding).", 0));
        }

        _thread = null;
        _slots = [];
        _universes = [];
    }

    private void Run()
    {
        var timerBegun = TimerResolution.Begin();
        var open = false;
        try
        {
            var refreshTicks = Stopwatch.Frequency / _frameRateHz;
            var minTicks = Stopwatch.Frequency / OutputConfig.MaxFrameRateHz;
            var lastSend = Stopwatch.GetTimestamp() - refreshTicks;
            var windowStart = 0L;
            var windowFrames = 0;

            while (!_stopRequested)
            {
                if (!open)
                {
                    open = TryOpen();
                    _firstOpenAttempt?.TrySetResult();
                    if (!open)
                    {
                        WaitBeforeRetry();
                        continue;
                    }

                    windowStart = Stopwatch.GetTimestamp();
                    windowFrames = 0;
                }

                var now = Stopwatch.GetTimestamp();
                var dueRefresh = lastSend + refreshTicks;
                if (now < dueRefresh)
                {
                    var signalled = _wake.WaitOne(TicksToWaitMilliseconds(dueRefresh - now));
                    if (_stopRequested)
                    {
                        break;
                    }

                    now = Stopwatch.GetTimestamp();
                    if (signalled)
                    {
                        // Changed data or a flush request: send early, but never above the max frame rate.
                        var earliest = lastSend + minTicks;
                        if (now < earliest)
                        {
                            Thread.Sleep(TicksToWaitMilliseconds(earliest - now));
                        }
                    }
                    else if (now < dueRefresh)
                    {
                        continue;
                    }
                }

                lock (_flushGate)
                {
                    (_cycleFlushes, _pendingFlushes) = (_pendingFlushes, _cycleFlushes);
                }

                lastSend = Stopwatch.GetTimestamp();
                if (!SendCycle())
                {
                    open = false;
                    CloseQuietly();
                    CompleteFlushes(_cycleFlushes, false);
                    continue;
                }

                CompleteFlushes(_cycleFlushes, true);
                windowFrames++;
                var elapsed = lastSend - windowStart;
                if (elapsed >= Stopwatch.Frequency)
                {
                    var fps = Math.Round(windowFrames * (double)Stopwatch.Frequency / elapsed, 1);
                    windowStart = lastSend;
                    windowFrames = 0;
                    SetStatus(OutputStatus.Sending(fps));
                }
            }
        }
        catch (Exception e)
        {
            // Defensive: a bug in the loop must not take the process down; report it visibly.
            SetStatus(OutputStatus.Error(DescribeError(e)));
        }
        finally
        {
            if (open)
            {
                try
                {
                    BeforeClose();
                }
                catch (Exception e)
                {
                    Trace.TraceWarning("{0}: final packets failed: {1}", _name, e.Message);
                }

                CloseQuietly();
            }

            lock (_flushGate)
            {
                _acceptingFlushes = false;
                CompleteFlushes(_pendingFlushes, false);
                CompleteFlushes(_cycleFlushes, false);
            }

            SetStatus(OutputStatus.Disabled);
            TimerResolution.End(timerBegun);
            _firstOpenAttempt?.TrySetResult();
            _threadExited?.TrySetResult();
        }
    }

    private bool TryOpen()
    {
        try
        {
            OpenTransport();
            SetStatus(OutputStatus.Sending(0));
            return true;
        }
        catch (Exception e)
        {
            CloseQuietly();
            SetStatus(OutputStatus.Error(DescribeError(e)));
            return false;
        }
    }

    private bool SendCycle()
    {
        var slots = _slots;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            int length;
            lock (slot)
            {
                if (!slot.HasData)
                {
                    continue;
                }

                length = slot.Length;
                slot.Pending.AsSpan(0, length).CopyTo(slot.Sending);
            }

            slot.SentLength = length;
            try
            {
                SendUniverse(i, slot.Sending.AsSpan(0, length));
            }
            catch (Exception e)
            {
                SetStatus(OutputStatus.Error(DescribeError(e)));
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Waits <see cref="RetryDelay"/> without reacting to data changes (which would hammer a missing device at the
    /// engine rate); only stop ends the wait early. Flushes fail immediately while there is no transport.
    /// </summary>
    private void WaitBeforeRetry()
    {
        var retryAt = Stopwatch.GetTimestamp() + (long)(RetryDelay.TotalSeconds * Stopwatch.Frequency);
        while (!_stopRequested)
        {
            FailPendingFlushes();
            var remaining = retryAt - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                break;
            }

            _wake.WaitOne(TicksToWaitMilliseconds(remaining));
        }
    }

    private void FailPendingFlushes()
    {
        lock (_flushGate)
        {
            CompleteFlushes(_pendingFlushes, false);
        }
    }

    private void CloseQuietly()
    {
        try
        {
            CloseTransport();
        }
        catch (Exception e)
        {
            Trace.TraceWarning("{0}: closing the transport failed: {1}", _name, e.Message);
        }
    }

    private void SetStatus(OutputStatus status)
    {
        lock (_statusGate)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
        }

        try
        {
            StatusChanged?.Invoke(this, status);
        }
        catch (Exception e)
        {
            // A faulty subscriber must not stop DMX transmission.
            Trace.TraceError("{0}: StatusChanged handler failed: {1}", _name, e);
        }
    }

    /// <summary>
    /// Per-universe double buffer: <see cref="Pending"/> is written by <see cref="Submit"/> (under the slot lock),
    /// <see cref="Sending"/> is owned by the send thread.
    /// </summary>
    private sealed class UniverseSlot(int universe)
    {
        public int Universe { get; } = universe;

        public byte[] Pending { get; } = new byte[DmxAddress.UniverseSize];

        public byte[] Sending { get; } = new byte[DmxAddress.UniverseSize];

        public int Length { get; set; }

        public bool HasData { get; set; }

        public int SentLength { get; set; }
    }
}
