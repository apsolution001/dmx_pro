namespace DmxPro.Engine;

/// <summary>
/// Receives every merged frame the engine computes. Outputs (Art-Net, sACN, USB-DMX) implement this to copy the latest
/// frame and transmit it at their own rate.
/// </summary>
/// <remarks>
/// Called on the engine thread once per tick and per universe. Implementations must copy the data, return quickly, never
/// block and never allocate. Exceptions are caught and logged by the engine, but must not be used for flow control.
/// </remarks>
public interface IFrameSink
{
    /// <summary>A freshly merged frame for one universe.</summary>
    /// <param name="universe">Project universe number, starting at 1 (protocol numbering is the output's job).</param>
    /// <param name="data">The 512 channel values; only valid during the call.</param>
    void OnFrame(int universe, ReadOnlySpan<byte> data);
}
