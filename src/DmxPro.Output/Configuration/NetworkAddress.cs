using System.Net;
using System.Net.Sockets;

namespace DmxPro.Output.Configuration;

/// <summary>Parsing helpers for IPv4 addresses typed by the user.</summary>
internal static class NetworkAddress
{
    /// <summary>Accepts only dotted-quad IPv4 (so "10" is not silently read as 0.0.0.10).</summary>
    public static bool TryParseIPv4(string? text, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Split('.').Length == 4
            && IPAddress.TryParse(trimmed, out var parsed)
            && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Bind address for a socket: the chosen adapter, or any adapter when empty.</summary>
    public static bool TryParseBind(string? text, out IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            address = IPAddress.Any;
            return true;
        }

        return TryParseIPv4(text, out address);
    }
}
