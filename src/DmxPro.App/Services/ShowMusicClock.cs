namespace DmxPro.App.Services;

/// <summary>
/// Estimates where the engine's show clock is <em>now</em> from its snapshots. Pure and allocation-free (no WPF), so
/// it can be unit-tested; time is any monotonic seconds value (the service uses a <see cref="System.Diagnostics.Stopwatch"/>).
/// </summary>
/// <remarks>
/// Engine snapshots are published at ~22–30 Hz and are read some unknown time after they were taken, so a raw snapshot
/// position lags reality by 0..~50 ms — far too noisy for a 25 ms sync threshold. A playing show advances at exactly real
/// time (the engine thread advances it by Stopwatch-measured deltas), so <c>engine(t) = t + c</c> for a constant
/// <c>c</c> between transport commands. Every fresh snapshot read at time <c>t</c> gives <c>position − t ≤ c</c> (it can
/// only be older than <c>t</c>), so the maximum over fresh snapshots converges on <c>c</c> from below within a few
/// snapshots. <see cref="Reset"/> must be called on every command that moves the clock (start, pause, resume, seek).
/// </remarks>
internal sealed class ShowMusicClock
{
    /// <summary>A fresh sample this far below the estimate means the clock really jumped back (a missed reset).</summary>
    private const double DiscontinuitySeconds = 0.25;

    private double _anchorPosition;
    private double _anchorTime;
    private bool _running;
    private bool _hasSample;
    private double _offset;
    private double _pausedPosition;
    private long _lastSequence = long.MinValue;

    /// <summary>True once a confirmed engine snapshot was seen since the last <see cref="Reset"/>.</summary>
    public bool HasSample => _hasSample;

    /// <summary>A command moved the clock to <paramref name="position"/> at <paramref name="now"/>.</summary>
    public void Reset(double position, double now, bool running)
    {
        _anchorPosition = position;
        _anchorTime = now;
        _running = running;
        _hasSample = false;
        _pausedPosition = position;
        _lastSequence = long.MinValue;
    }

    /// <summary>Feeds a confirmed engine snapshot (<paramref name="sequence"/> = its number) read at <paramref name="now"/>.</summary>
    public void Observe(double position, long sequence, double now)
    {
        if (sequence == _lastSequence || !double.IsFinite(position))
        {
            return;
        }

        _lastSequence = sequence;
        if (!_running)
        {
            _pausedPosition = position;
            _hasSample = true;
            return;
        }

        var offset = position - now;
        if (!_hasSample || offset > _offset || offset < _offset - DiscontinuitySeconds)
        {
            _offset = offset;
        }

        _hasSample = true;
    }

    /// <summary>The show position now.</summary>
    public double Estimate(double now)
    {
        if (!_running)
        {
            return _pausedPosition;
        }

        return _hasSample ? now + _offset : _anchorPosition + Math.Max(0, now - _anchorTime);
    }
}
