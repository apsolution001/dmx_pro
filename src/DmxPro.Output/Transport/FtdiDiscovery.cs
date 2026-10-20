using System.Text;

namespace DmxPro.Output.Transport;

/// <summary>Lists FTDI devices for the Open DMX device picker.</summary>
public static class FtdiDiscovery
{
    internal const uint FlagOpened = 1;

    /// <summary>
    /// Lists devices. Returns <c>false</c> with a user-facing <paramref name="error"/> when the D2XX driver is missing
    /// or the call fails; never throws.
    /// </summary>
    public static bool TryListDevices(out IReadOnlyList<FtdiDeviceInfo> devices, out string? error)
    {
        devices = [];
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "Open DMX is only supported on Windows.";
            return false;
        }

        try
        {
            var status = FtdiNative.FT_CreateDeviceInfoList(out var count);
            if (status != FtdiNative.Ok)
            {
                error = $"Could not list FTDI devices: {FtdiNative.DescribeStatus(status)}.";
                return false;
            }

            var list = new List<FtdiDeviceInfo>((int)count);
            var serial = new byte[16];
            var description = new byte[64];
            for (uint i = 0; i < count; i++)
            {
                Array.Clear(serial);
                Array.Clear(description);
                status = FtdiNative.FT_GetDeviceInfoDetail(i, out var flags, out _, out _, out _, serial, description, out _);
                if (status == FtdiNative.Ok)
                {
                    list.Add(new FtdiDeviceInfo((int)i, ReadCString(serial), ReadCString(description), (flags & FlagOpened) != 0));
                }
            }

            devices = list;
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = FtdiNative.DriverMissingMessage;
            return false;
        }
    }

    private static string ReadCString(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.ASCII.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }
}
