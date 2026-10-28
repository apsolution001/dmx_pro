namespace DmxPro.App.Tests;

/// <summary>
/// Minimal fake clock: time only moves in <see cref="Advance"/>, which fires due timers inline on the calling thread.
/// Supports one-shot and periodic timers created through <see cref="CreateTimer"/>. Timers may be created or disposed
/// from other threads (async continuations) while a test advances time.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Timers that are still scheduled (not disposed, not finished).</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_gate)
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
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves time forward, firing every timer that becomes due, in due order.</summary>
    public void Advance(TimeSpan delta)
    {
        DateTimeOffset target;
        lock (_gate)
        {
            target = _now + delta;
        }

        while (true)
        {
            ManualTimer? next = null;
            lock (_gate)
            {
                foreach (var timer in _timers)
                {
                    if (timer.DueAt is { } due && due <= target && (next is null || due < next.DueAt))
                    {
                        next = timer;
                    }
                }

                if (next is null)
                {
                    _now = target;
                    break;
                }

                _now = next.DueAt!.Value;
                next.Reschedule();
            }

            // Outside the lock: the callback may create or dispose timers.
            next.Invoke();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                _period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            }

            return true;
        }

        /// <summary>Called under the owner's lock when the timer fires.</summary>
        public void Reschedule() =>
            DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : DueAt + _period;

        public void Invoke() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
            {
                DueAt = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
