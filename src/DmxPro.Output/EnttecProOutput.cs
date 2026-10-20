using DmxPro.Output.Configuration;
using DmxPro.Output.Protocols;
using DmxPro.Output.Transport;

namespace DmxPro.Output;

/// <summary>
/// Enttec DMX USB Pro (and compatibles) via its virtual COM port: one label-6 "send DMX" message per frame. The
/// widget's own microcontroller generates DMX timing. Reconnects automatically after the device is re-plugged.
/// </summary>
public sealed class EnttecProOutput : DmxOutputBase<EnttecProOutputConfig>
{
    private readonly ISerialPortTransport _port;
    private readonly byte[] _packet = new byte[EnttecProPacket.MaxPacketLength];
    private string _portName = string.Empty;

    public EnttecProOutput()
        : this(new Win32SerialPort())
    {
    }

    internal EnttecProOutput(ISerialPortTransport port)
        : base("Enttec DMX USB Pro")
    {
        _port = port;
    }

    protected override void Prepare(EnttecProOutputConfig config, IReadOnlyList<int> universes) =>
        _portName = config.PortName.Trim();

    protected override void OpenTransport() => _port.Open(_portName);

    protected override void SendUniverse(int slotIndex, ReadOnlySpan<byte> data)
    {
        var length = EnttecProPacket.WriteSendDmx(_packet, data);
        _port.Write(_packet.AsSpan(0, length));
    }

    protected override void CloseTransport() => _port.Close();

    protected override void DisposeResources() => _port.Dispose();
}
