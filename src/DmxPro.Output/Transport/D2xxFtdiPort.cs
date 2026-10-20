using System.Runtime.InteropServices;
using System.Text;

namespace DmxPro.Output.Transport;

/// <summary>FTDI device opened through D2XX and configured for DMX512: 250 kbaud, 8 data bits, 2 stop bits, no parity.</summary>
internal sealed class D2xxFtdiPort : IFtdiPort
{
    public const uint DmxBaudRate = 250_000;
    internal const uint WriteTimeoutMilliseconds = 500;

    private IntPtr _handle;

    public void Open(string? serialNumber, int deviceIndex)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        Close();
        try
        {
            IntPtr handle;
            uint status;
            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                var serial = Encoding.ASCII.GetBytes(serialNumber.Trim() + "\0");
                status = FtdiNative.FT_OpenEx(serial, FtdiNative.OpenBySerialNumber, out handle);
            }
            else
            {
                status = FtdiNative.FT_Open(deviceIndex, out handle);
            }

            if (status != FtdiNative.Ok)
            {
                throw new OutputTransportException($"Open DMX: {FtdiNative.DescribeStatus(status)}. Retrying…");
            }

            _handle = handle;
            Check(FtdiNative.FT_ResetDevice(handle));
            Check(FtdiNative.FT_SetBaudRate(handle, DmxBaudRate));
            Check(FtdiNative.FT_SetDataCharacteristics(handle, FtdiNative.Bits8, FtdiNative.StopBits2, FtdiNative.ParityNone));
            Check(FtdiNative.FT_SetFlowControl(handle, FtdiNative.FlowNone, 0, 0));
            Check(FtdiNative.FT_ClrRts(handle)); // RTS low enables the RS-485 driver on Open DMX USB
            Check(FtdiNative.FT_Purge(handle, FtdiNative.PurgeRx | FtdiNative.PurgeTx));
            Check(FtdiNative.FT_SetTimeouts(handle, 0, WriteTimeoutMilliseconds));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Close();
            throw new OutputTransportException(FtdiNative.DriverMissingMessage, e);
        }
        catch
        {
            Close();
            throw;
        }
    }

    public void SetBreak(bool on) =>
        Check(on ? FtdiNative.FT_SetBreakOn(_handle) : FtdiNative.FT_SetBreakOff(_handle));

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        Check(FtdiNative.FT_Write(_handle, ref MemoryMarshal.GetReference(data), (uint)data.Length, out var written));
        if (written != data.Length)
        {
            throw new OutputTransportException("Open DMX: the device stopped accepting data (write timed out). Retrying…");
        }
    }

    public void Close()
    {
        if (_handle != IntPtr.Zero)
        {
            _ = FtdiNative.FT_Close(_handle);
            _handle = IntPtr.Zero;
        }
    }

    public void Dispose() => Close();

    private static void Check(uint status)
    {
        if (status != FtdiNative.Ok)
        {
            throw new OutputTransportException($"Open DMX: {FtdiNative.DescribeStatus(status)}. Retrying…");
        }
    }
}
