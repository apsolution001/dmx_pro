namespace DmxPro.App.Tests;

/// <summary>
/// Minimal fake clock: time only moves in <see cref="Advance"/>, which fires due timers inline on the calling thread.
/// Supports one-shot and periodic timers created through <see cref="CreateTimer"/>.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Timers that are still scheduled (not disposed, not finished).</summary>
    public int ActiveTimerCount
    {
        get
        {
            var count = 0;
            foreach (var timer in _timers)
            {
                if (timer.DueAt is not null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves time forward, firing every timer that becomes due, in due order.</summary>
    public void Advance(TimeSpan delta)
    {
        var target = _now + delta;
        while (true)
        {
            ManualTimer? next = null;
            foreach (var timer in _timers)
            {
                if (timer.DueAt is { } due && due <= target && (next is null || due < next.DueAt))
                {
                    next = timer;
                }
            }

            if (next is null)
            {
                break;
            }

            _now = next.DueAt!.Value;
            next.Fire();
        }

        _now = target;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Fire()
        {
            DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : DueAt + _period;
            callback(state);
        }

        public void Dispose()
        {
            DueAt = null;
            owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
