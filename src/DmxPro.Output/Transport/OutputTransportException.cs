namespace DmxPro.Output.Transport;

/// <summary>
/// A transport failure whose <see cref="Exception.Message"/> is already a user-facing explanation. Thrown only inside
/// the output's own thread and turned into an Error status; never escapes to the engine.
/// </summary>
public sealed class OutputTransportException : Exception
{
    public OutputTransportException(string message)
        : base(message)
    {
    }

    public OutputTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
