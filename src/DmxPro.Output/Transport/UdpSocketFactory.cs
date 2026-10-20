using System.Net;
using System.Net.Sockets;

namespace DmxPro.Output.Transport;

/// <summary>Creates send-only UDP sockets for Art-Net / sACN.</summary>
internal static class UdpSocketFactory
{
    /// <summary>SIO_UDP_CONNRESET: stop ICMP "port unreachable" from failing later socket calls on Windows.</summary>
    internal const int SioUdpConnReset = -1744830452;

    public static Socket Create(IPAddress bindAddress, bool enableBroadcast)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.EnableBroadcast = enableBroadcast;
            if (OperatingSystem.IsWindows())
            {
                socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            }

            // Ephemeral source port: other lighting software on this PC may already own 6454 / 5568.
            socket.Bind(new IPEndPoint(bindAddress, 0));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
