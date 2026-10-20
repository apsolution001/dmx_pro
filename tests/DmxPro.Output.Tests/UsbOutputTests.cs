using System.Runtime.InteropServices;
using DmxPro.Output.Configuration;
using DmxPro.Output.Protocols;
using DmxPro.Output.Transport;

namespace DmxPro.Output.Tests;

public class UsbOutputTests
{
    [Fact]
    public async Task EnttecPro_Submit_WritesLabel6Message()
    {
        var port = new FakeSerialPort();
        await using var output = new EnttecProOutput(port);
        await output.StartAsync(new EnttecProOutputConfig { PortName = " COM7 " });
        var frame = TestFrames.Ramp(512);

        output.Submit(1, frame);
        Assert.True(await output.FlushAsync());

        Assert.Equal("COM7", port.OpenedPort);
        var expected = new byte[EnttecProPacket.MaxPacketLength];
        EnttecProPacket.WriteSendDmx(expected, frame);
        Assert.Contains(port.Writes, w => w.AsSpan().SequenceEqual(expected));
    }

    [Fact]
    public async Task EnttecPro_PortMissing_ReportsErrorAndRetries()
    {
        var port = new FakeSerialPort
        {
            OpenFailure = () => new OutputTransportException("COM9 not found. Is the USB-DMX interface plugged in? Retrying…"),
        };
        await using var output = new EnttecProOutput(port) { RetryDelay = TimeSpan.FromMilliseconds(20) };

        await output.StartAsync(new EnttecProOutputConfig { PortName = "COM9" });

        Assert.Equal(OutputState.Error, output.Status.State);
        Assert.Contains("COM9 not found", output.Status.Message);
        Assert.True(await TestFrames.WaitUntilAsync(() => port.OpenAttempts >= 3));
        Assert.False(await output.FlushAsync());
    }

    [Fact]
    public async Task EnttecPro_DeviceUnpluggedThenReplugged_RecoversToSending()
    {
        var unplugged = 1;
        var port = new FakeSerialPort
        {
            WriteFailure = () => Volatile.Read(ref unplugged) == 1 ? new IOException("gone") : null,
        };
        await using var output = new EnttecProOutput(port) { RetryDelay = TimeSpan.FromMilliseconds(20) };
        string? errorMessage = null;
        output.StatusChanged += (_, s) =>
        {
            if (s.State == OutputState.Error)
            {
                Volatile.Write(ref errorMessage, s.Message);
            }
        };
        await output.StartAsync(new EnttecProOutputConfig { PortName = "COM3" });

        output.Submit(1, TestFrames.Filled(5));
        Assert.True(await TestFrames.WaitUntilAsync(() => Volatile.Read(ref errorMessage) is not null));
        Assert.Contains("gone", errorMessage);

        Volatile.Write(ref unplugged, 0);
        Assert.True(await TestFrames.WaitUntilAsync(() => output.Status.State == OutputState.Sending && !port.Writes.IsEmpty));
    }

    [Fact]
    public async Task OpenDmx_Submit_SendsBreakThenStartCodeAndFullFrame()
    {
        var port = new FakeFtdiPort();
        await using var output = new OpenDmxOutput(port);
        await output.StartAsync(new OpenDmxOutputConfig { SerialNumber = "AB12" });

        output.Submit(1, [11, 22, 33]);
        Assert.True(await output.FlushAsync());

        var calls = port.Calls.ToArray();
        Assert.Equal("open:AB12", calls[0]);
        Assert.Equal(["break-on", "break-off", "write:513"], calls[1..4]);
        var written = port.Writes.First();
        Assert.Equal(new byte[] { 0, 11, 22, 33, 0 }, written[..5]);
        Assert.All(written[4..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void D2xxPort_DriverNotInstalled_ThrowsUserFacingMessage()
    {
        if (!OperatingSystem.IsWindows() || NativeLibrary.TryLoad("ftd2xx.dll", out var handle) && Free(handle))
        {
            return; // Driver present (owner's PC): opening would touch real hardware.
        }

        using var port = new D2xxFtdiPort();

        var e = Assert.Throws<OutputTransportException>(() => port.Open(null, 0));
        Assert.Contains("ftd2xx.dll", e.Message);
    }

    private static bool Free(IntPtr handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }
}
