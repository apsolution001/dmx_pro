using DmxPro.Output.Configuration;

namespace DmxPro.Output;

/// <summary>
/// One DMX transport (Art-Net, sACN, USB interface, virtual). The engine hands it finished universe frames via
/// <see cref="Submit"/>; the output transmits them on its own thread at its configured rate.
/// </summary>
/// <remarks>
/// Contract: no member throws into the caller. Hardware/network problems set <see cref="Status"/> to
/// <see cref="OutputState.Error"/> with a human message (safety rule 10) and the output retries in the background.
/// <see cref="StatusChanged"/> is raised on a background thread — UI subscribers must marshal to the dispatcher.
/// </remarks>
public interface IDmxOutput : IAsyncDisposable
{
    /// <summary>Display name (the configuration's name once started).</summary>
    string Name { get; }

    /// <summary>Current state; <see cref="OutputState.Disabled"/> until <see cref="StartAsync"/> is called.</summary>
    OutputStatus Status { get; }

    /// <summary>Project universes (from 1) this output transmits; empty while stopped.</summary>
    IReadOnlyList<int> Universes { get; }

    /// <summary>Raised (on a background thread) whenever <see cref="Status"/> changes.</summary>
    event EventHandler<OutputStatus>? StatusChanged;

    /// <summary>
    /// Validates <paramref name="config"/> and starts transmitting. An invalid configuration leaves the output stopped
    /// with an Error status (safety rule 2). Completes after the first attempt to open the transport.
    /// </summary>
    Task StartAsync(OutputConfig config, CancellationToken cancellationToken = default);

    /// <summary>Stops transmission (this is "output disabled", not blackout) and closes the transport.</summary>
    Task StopAsync();

    /// <summary>
    /// Copies <paramref name="frame"/> (up to 512 slot values, start code excluded) for project universe
    /// <paramref name="projectUniverse"/> into a pre-allocated buffer. Frames for universes this output does not carry
    /// are ignored. Allocation-free and non-blocking apart from a short buffer lock.
    /// </summary>
    void Submit(int projectUniverse, ReadOnlySpan<byte> frame);

    /// <summary>
    /// Completes with <c>true</c> once every frame submitted before the call has been transmitted at least once, or
    /// <c>false</c> if the output is stopped or failed to transmit.
    /// </summary>
    Task<bool> FlushAsync(CancellationToken cancellationToken = default);
}
