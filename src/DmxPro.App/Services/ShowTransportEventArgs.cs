namespace DmxPro.App.Services;

/// <summary>A show transport change (see <see cref="IConsolePlayback.ShowTransport"/>).</summary>
public sealed class ShowTransportEventArgs(ShowTransportKind kind, Guid showId, double positionSeconds = 0) : EventArgs
{
    public ShowTransportKind Kind { get; } = kind;

    /// <summary>The show, or <see cref="Guid.Empty"/> for <see cref="ShowTransportKind.AllStopped"/>.</summary>
    public Guid ShowId { get; } = showId;

    /// <summary>The commanded show position in seconds (start, pause, resume and seek; else 0).</summary>
    public double PositionSeconds { get; } = positionSeconds;
}
