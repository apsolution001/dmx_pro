using DmxPro.Core.Patching;
using DmxPro.Output.Configuration;

namespace DmxPro.Output;

/// <summary>
/// Loopback output: "transmits" by storing the last frame per universe. Used for the DMX monitor, the UI without
/// hardware, and tests. Runs through the same send thread and rate as real outputs.
/// </summary>
public sealed class VirtualOutput : DmxOutputBase<VirtualOutputConfig>
{
    private readonly object _framesGate = new();
    private int[] _universes = [];
    private byte[][] _lastFrames = [];
    private int[] _lastLengths = [];
    private long _framesSent;

    public VirtualOutput()
        : base("Virtual output")
    {
    }

    /// <summary>Number of universe frames transmitted since start.</summary>
    public long FramesSent => Interlocked.Read(ref _framesSent);

    /// <summary>Copies the last transmitted frame of <paramref name="projectUniverse"/> into <paramref name="destination"/>.</summary>
    /// <returns><c>false</c> if nothing has been transmitted for that universe.</returns>
    public bool TryGetLastFrame(int projectUniverse, Span<byte> destination, out int length)
    {
        lock (_framesGate)
        {
            for (var i = 0; i < _universes.Length; i++)
            {
                if (_universes[i] == projectUniverse && _lastLengths[i] > 0)
                {
                    length = Math.Min(_lastLengths[i], destination.Length);
                    _lastFrames[i].AsSpan(0, length).CopyTo(destination);
                    return true;
                }
            }
        }

        length = 0;
        return false;
    }

    protected override void Prepare(VirtualOutputConfig config, IReadOnlyList<int> universes)
    {
        var frames = new byte[universes.Count][];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = new byte[DmxAddress.UniverseSize];
        }

        lock (_framesGate)
        {
            _universes = [.. universes];
            _lastFrames = frames;
            _lastLengths = new int[universes.Count];
        }

        Interlocked.Exchange(ref _framesSent, 0);
    }

    protected override void OpenTransport()
    {
    }

    protected override void SendUniverse(int slotIndex, ReadOnlySpan<byte> data)
    {
        lock (_framesGate)
        {
            data.CopyTo(_lastFrames[slotIndex]);
            _lastLengths[slotIndex] = data.Length;
        }

        Interlocked.Increment(ref _framesSent);
    }

    protected override void CloseTransport()
    {
    }
}
