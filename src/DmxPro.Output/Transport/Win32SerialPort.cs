using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DmxPro.Output.Transport;

/// <summary>
/// Write-only COM port via Win32 (avoids the System.IO.Ports NuGet package). Used for the Enttec DMX USB Pro, whose
/// microcontroller generates DMX timing itself, so only the framed messages matter and the line settings are nominal.
/// </summary>
internal sealed class Win32SerialPort : ISerialPortTransport
{
    /// <summary>Ignored by the Pro (FT245 FIFO) but set to a sane value for UART-based clones.</summary>
    internal const uint BaudRate = 115200;

    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint OpenExisting = 3;
    internal const uint PurgeTxClear = 0x0004;
    internal const uint PurgeRxClear = 0x0008;

    // DCB flag bits: fBinary | fDtrControl = DTR_CONTROL_ENABLE | fRtsControl = RTS_CONTROL_ENABLE.
    internal const uint DcbFlags = 0x0001 | 0x0010 | 0x1000;

    internal const int ErrorFileNotFound = 2;
    internal const int ErrorPathNotFound = 3;
    internal const int ErrorAccessDenied = 5;

    /// <summary>A stuck device must not block the output thread forever.</summary>
    internal const uint WriteTimeoutMilliseconds = 500;

    private SafeFileHandle? _handle;
    private string _portName = string.Empty;

    public void Open(string portName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        Close();
        _portName = portName.Trim();
        var handle = CreateFileW(@"\\.\" + _portName, GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw error switch
            {
                ErrorFileNotFound or ErrorPathNotFound => new OutputTransportException(
                    $"{_portName} not found. Is the USB-DMX interface plugged in? Retrying…"),
                ErrorAccessDenied => new OutputTransportException(
                    $"{_portName} is in use by another program. Close other lighting software."),
                _ => new OutputTransportException($"Cannot open {_portName} (Windows error {error})."),
            };
        }

        try
        {
            var dcb = new Dcb { DcbLength = (uint)Marshal.SizeOf<Dcb>() };
            if (!GetCommState(handle, ref dcb))
            {
                throw Failure("read the settings of");
            }

            dcb.BaudRate = BaudRate;
            dcb.Flags = DcbFlags;
            dcb.ByteSize = 8;
            dcb.Parity = 0;   // NOPARITY
            dcb.StopBits = 2; // TWOSTOPBITS
            if (!SetCommState(handle, ref dcb))
            {
                throw Failure("configure");
            }

            var timeouts = new CommTimeouts
            {
                ReadIntervalTimeout = uint.MaxValue, // reads return immediately (we never read)
                WriteTotalTimeoutConstant = WriteTimeoutMilliseconds,
            };
            if (!SetCommTimeouts(handle, ref timeouts))
            {
                throw Failure("configure");
            }

            _ = PurgeComm(handle, PurgeTxClear | PurgeRxClear);
            _handle = handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var handle = _handle ?? throw new OutputTransportException("The COM port is not open.");
        if (data.IsEmpty)
        {
            return;
        }

        if (!WriteFile(handle, ref MemoryMarshal.GetReference(data), (uint)data.Length, out var written, IntPtr.Zero))
        {
            throw new OutputTransportException(
                $"Lost connection to {_portName} (Windows error {Marshal.GetLastWin32Error()}). " +
                "Was the interface unplugged? Retrying…");
        }

        if (written != data.Length)
        {
            throw new OutputTransportException($"{_portName} stopped accepting data (write timed out). Retrying…");
        }
    }

    public void Close()
    {
        _handle?.Dispose();
        _handle = null;
    }

    public void Dispose() => Close();

    private OutputTransportException Failure(string action) =>
        new($"Cannot {action} {_portName} (Windows error {Marshal.GetLastWin32Error()}).");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle file, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeComm(SafeFileHandle file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle file,
        ref byte buffer,
        uint numberOfBytesToWrite,
        out uint numberOfBytesWritten,
        IntPtr overlapped);

    /// <summary>Win32 DCB (28 bytes). Bit fields are packed into <see cref="Flags"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint DcbLength;
        public uint BaudRate;
        public uint Flags;
        public ushort Reserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }
}
