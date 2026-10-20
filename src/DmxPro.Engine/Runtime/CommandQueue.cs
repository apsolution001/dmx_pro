namespace DmxPro.Engine.Runtime;

/// <summary>
/// Multi-producer, single-consumer command queue. Producers append under a lock; the engine swaps the two lists under
/// the same lock (a pointer swap) and processes its list without holding the lock. Lists keep their capacity, so the
/// steady state allocates nothing.
/// </summary>
internal sealed class CommandQueue
{
    private readonly object _gate = new();
    private List<EngineCommand> _pending = new(64);
    private List<EngineCommand> _processing = new(64);

    public void Enqueue(in EngineCommand command)
    {
        lock (_gate)
        {
            _pending.Add(command);
        }
    }

    /// <summary>Takes everything queued so far. The returned list belongs to the engine thread until the next call.</summary>
    public List<EngineCommand> Drain()
    {
        _processing.Clear();
        lock (_gate)
        {
            (_pending, _processing) = (_processing, _pending);
        }

        return _processing;
    }
}
