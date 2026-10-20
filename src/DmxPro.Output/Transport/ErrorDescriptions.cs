using System.Net.Sockets;

namespace DmxPro.Output.Transport;

/// <summary>Turns transport exceptions into plain-language status messages (safety rule 10).</summary>
internal static class ErrorDescriptions
{
    public static string Describe(Exception exception) => exception switch
    {
        OutputTransportException e => e.Message,
        SocketException { SocketErrorCode: SocketError.AddressNotAvailable } =>
            "The selected network adapter address is not available on this PC. Check the adapter in Input/Output.",
        SocketException { SocketErrorCode: SocketError.NetworkUnreachable or SocketError.HostUnreachable } =>
            "Network unreachable. Check the cable / Wi-Fi and that the PC and node are on the same subnet.",
        SocketException { SocketErrorCode: SocketError.AccessDenied } =>
            "Windows refused to send (firewall or broadcast not allowed on this adapter).",
        SocketException { SocketErrorCode: SocketError.NetworkDown } =>
            "The network adapter is down. Reconnect the network.",
        SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } =>
            "The network port is already used by another program.",
        SocketException e => $"Network error: {e.Message}",
        DllNotFoundException =>
            "A required driver library is missing. Install the device driver and restart DMX Pro.",
        PlatformNotSupportedException => "This output type is only supported on Windows.",
        UnauthorizedAccessException => "The device is in use by another program. Close other lighting software.",
        IOException e => $"Device error: {e.Message}",
        _ => $"Output error: {exception.Message}",
    };
}
