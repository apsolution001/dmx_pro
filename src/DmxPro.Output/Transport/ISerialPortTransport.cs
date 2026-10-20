namespace DmxPro.Output.Transport;

/// <summary>Minimal write-only serial port, so the Enttec Pro output can be tested without hardware.</summary>
internal interface ISerialPortTransport : IDisposable
{
    /// <summary>Opens <paramref name="portName"/>; throws <see cref="OutputTransportException"/> with a user message.</summary>
    void Open(string portName);

    /// <summary>Writes all bytes or throws.</summary>
    void Write(ReadOnlySpan<byte> data);

    void Close();
}
