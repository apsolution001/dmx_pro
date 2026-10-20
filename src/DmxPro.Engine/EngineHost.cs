using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DmxPro.Engine;

/// <summary>
/// Runs <see cref="DmxEngine.Tick"/> on a dedicated background thread at a fixed rate (default 44 Hz), timed with a
/// monotonic <see cref="Stopwatch"/>. Each tick receives the real elapsed time, so fades and shows stay correct even
/// when the OS wakes the thread late. Exceptions from a tick are logged and the loop keeps running.
/// </summary>
public sealed class EngineHost : IDisposable
{
    /// <summary>Default tick rate (the classic DMX refresh rate for a full universe).</summary>
    public const double DefaultTickRateHz = 44;

    private readonly DmxEngine _engine;
    private readonly ILogger _logger;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private ManualResetEventSlim? _stopSignal;
    private Thread? _thread;

    /// <summary>Creates a host for <paramref name="engine"/>; call <see cref="Start"/> to begin ticking.</summary>
    /// <param name="engine">Engine to drive.</param>
    /// <param name="logger">Logger for tick failures.</param>
    /// <param name="tickRateHz">Ticks per second, 1–1000.</param>
    public EngineHost(DmxEngine engine, ILogger<EngineHost>? logger = null, double tickRateHz = DefaultTickRateHz)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!double.IsFinite(tickRateHz) || tickRateHz < 1 || tickRateHz > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(tickRateHz), tickRateHz, "Tick rate must be 1–1000 Hz.");
        }

        _engine = engine;
        _logger = logger ?? NullLogger<EngineHost>.Instance;
        _interval = TimeSpan.FromSeconds(1.0 / tickRateHz);
    }

    /// <summary>True while the engine thread runs.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _thread is not null;
            }
        }
    }

    /// <summary>Starts the engine thread. Does nothing when it already runs.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_thread is not null)
            {
                return;
            }

            _stopSignal = new ManualResetEventSlim(false);
            var signal = _stopSignal;
            _thread = new Thread(() => Run(signal))
            {
                Name = "DMX engine",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
        }
    }

    /// <summary>
    /// Stops the engine thread, then releases everything and renders one last safe frame to the frame sinks
    /// (safety rule 6: stop functions → safe frame → the caller then stops output).
    /// </summary>
    public void Stop(SafeFrameMode safeFrame = SafeFrameMode.ProfileDefaults)
    {
        Thread? thread;
        ManualResetEventSlim? signal;
        lock (_gate)
        {
            thread = _thread;
            signal = _stopSignal;
            _thread = null;
            _stopSignal = null;
        }

        if (thread is not null)
        {
            signal!.Set();
            thread.Join();
            signal.Dispose();
        }

        try
        {
            _engine.ReleaseAll();
            if (safeFrame == SafeFrameMode.Blackout)
            {
                _engine.Blackout = true;
            }

            _engine.Tick(TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rendering the safe frame failed");
        }
    }

    /// <summary>Stops the loop with a profile-defaults safe frame.</summary>
    public void Dispose() => Stop();

    private void Run(ManualResetEventSlim stop)
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        var nextDeadline = last + _interval;

        while (!stop.IsSet)
        {
            var now = clock.Elapsed;
            try
            {
                _engine.Tick(now - last);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine tick failed");
            }

            last = now;

            // Fixed-rate schedule on absolute deadlines; if far behind (debugger, suspend) re-anchor instead of bursting.
            nextDeadline += _interval;
            var remaining = nextDeadline - clock.Elapsed;
            if (remaining < -(_interval * 5))
            {
                nextDeadline = clock.Elapsed + _interval;
                remaining = _interval;
            }

            if (remaining > TimeSpan.Zero && stop.Wait(remaining))
            {
                break;
            }
        }
    }
}
