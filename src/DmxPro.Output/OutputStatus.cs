namespace DmxPro.Output;

/// <summary>
/// Snapshot of an output's state. A value type so status updates from the send thread never allocate.
/// </summary>
/// <param name="State">Coarse state.</param>
/// <param name="Message">Human-readable explanation (always set for <see cref="OutputState.Error"/>).</param>
/// <param name="FramesPerSecond">Measured transmit rate (full cycles over all universes per second).</param>
public readonly record struct OutputStatus(OutputState State, string? Message, double FramesPerSecond)
{
    public static OutputStatus Disabled { get; } = new(OutputState.Disabled, null, 0);

    public static OutputStatus Connecting { get; } = new(OutputState.Connecting, null, 0);

    public static OutputStatus Error(string message) => new(OutputState.Error, message, 0);

    public static OutputStatus Sending(double framesPerSecond) => new(OutputState.Sending, null, framesPerSecond);
}
