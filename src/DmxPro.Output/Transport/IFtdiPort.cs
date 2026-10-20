namespace DmxPro.Output.Transport;

/// <summary>The FTDI operations Open DMX needs, so the frame sequence can be tested without hardware.</summary>
internal interface IFtdiPort : IDisposable
{
    /// <summary>Opens the device and configures 250 kbaud 8N2, no flow control.</summary>
    void Open(string? serialNumber, int deviceIndex);

    void SetBreak(bool on);

    /// <summary>Writes all bytes or throws.</summary>
    void Write(ReadOnlySpan<byte> data);

    void Close();
}
