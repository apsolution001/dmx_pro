using DmxPro.Core.Patching;
using DmxPro.Output.Configuration;
using DmxPro.Output.Transport;

namespace DmxPro.Output;

/// <summary>
/// Enttec Open DMX USB / generic FTDI dongle via the D2XX driver. The PC generates the DMX signal: BREAK (≥ 100 µs),
/// MARK AFTER BREAK (≥ 12 µs), then start code 0 + 512 slots at 250 kbaud 8N2. Best effort — timing can jitter
/// under Windows load. If <c>ftd2xx.dll</c> is not installed the output shows a clear Error and keeps retrying.
/// </summary>
public sealed class OpenDmxOutput : DmxOutputBase<OpenDmxOutputConfig>
{
    public const int BreakMicroseconds = 110;
    public const int MarkAfterBreakMicroseconds = 16;

    private readonly IFtdiPort _port;
    private readonly byte[] _frame = new byte[1 + DmxAddress.UniverseSize];
    private string? _serialNumber;
    private int _deviceIndex;

    public OpenDmxOutput()
        : this(new D2xxFtdiPort())
    {
    }

    internal OpenDmxOutput(IFtdiPort port)
        : base("Open DMX USB")
    {
        _port = port;
    }

    protected override void Prepare(OpenDmxOutputConfig config, IReadOnlyList<int> universes)
    {
        _serialNumber = string.IsNullOrWhiteSpace(config.SerialNumber) ? null : config.SerialNumber.Trim();
        _deviceIndex = config.DeviceIndex;
    }

    protected override void OpenTransport() => _port.Open(_serialNumber, _deviceIndex);

    protected override void SendUniverse(int slotIndex, ReadOnlySpan<byte> data)
    {
        // Always a full 512-slot frame: some fixtures misbehave on short frames and the time cost is fixed anyway.
        _frame[0] = 0x00; // start code
        data.CopyTo(_frame.AsSpan(1));
        _frame.AsSpan(1 + data.Length).Clear();

        _port.SetBreak(true);
        PreciseDelay.Microseconds(BreakMicroseconds);
        _port.SetBreak(false);
        PreciseDelay.Microseconds(MarkAfterBreakMicroseconds);
        _port.Write(_frame);
    }

    protected override void CloseTransport() => _port.Close();

    protected override void DisposeResources() => _port.Dispose();
}
