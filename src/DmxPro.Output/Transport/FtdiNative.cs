using System.Runtime.InteropServices;

namespace DmxPro.Output.Transport;

/// <summary>
/// P/Invoke declarations for the FTDI D2XX driver (<c>ftd2xx.dll</c>, installed with the FTDI driver package).
/// Every call can throw <see cref="DllNotFoundException"/> when the driver is not installed.
/// </summary>
internal static class FtdiNative
{
    public const string DriverMissingMessage =
        "FTDI D2XX driver (ftd2xx.dll) not found. Install the FTDI D2XX driver from ftdichip.com, then enable output again.";

    public const uint Ok = 0;
    public const uint InvalidHandle = 1;
    public const uint DeviceNotFound = 2;
    public const uint DeviceNotOpened = 3;
    public const uint IoError = 4;

    public const uint OpenBySerialNumber = 1;
    public const byte Bits8 = 8;
    public const byte StopBits2 = 2;
    public const byte ParityNone = 0;
    public const ushort FlowNone = 0;
    public const uint PurgeRx = 1;
    public const uint PurgeTx = 2;

    internal const string Library = "ftd2xx.dll";

    public static string DescribeStatus(uint status) => status switch
    {
        InvalidHandle or IoError => "the device stopped responding (was it unplugged?)",
        DeviceNotFound => "no FTDI device found (is the Open DMX interface plugged in?)",
        DeviceNotOpened => "the device could not be opened (in use by another program?)",
        _ => $"FTDI status {status}",
    };

    [DllImport(Library)]
    public static extern uint FT_CreateDeviceInfoList(out uint numberOfDevices);

    [DllImport(Library)]
    public static extern uint FT_GetDeviceInfoDetail(
        uint index,
        out uint flags,
        out uint type,
        out uint id,
        out uint locationId,
        byte[] serialNumber,
        byte[] description,
        out IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_Open(int deviceNumber, out IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_OpenEx(byte[] arg1, uint flags, out IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_Close(IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_ResetDevice(IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_SetBaudRate(IntPtr handle, uint baudRate);

    [DllImport(Library)]
    public static extern uint FT_SetDataCharacteristics(IntPtr handle, byte wordLength, byte stopBits, byte parity);

    [DllImport(Library)]
    public static extern uint FT_SetFlowControl(IntPtr handle, ushort flowControl, byte xon, byte xoff);

    [DllImport(Library)]
    public static extern uint FT_ClrRts(IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_Purge(IntPtr handle, uint mask);

    [DllImport(Library)]
    public static extern uint FT_SetTimeouts(IntPtr handle, uint readTimeout, uint writeTimeout);

    [DllImport(Library)]
    public static extern uint FT_SetBreakOn(IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_SetBreakOff(IntPtr handle);

    [DllImport(Library)]
    public static extern uint FT_Write(IntPtr handle, ref byte buffer, uint bytesToWrite, out uint bytesWritten);
}
