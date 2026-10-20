using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DmxPro.App.Services;

/// <summary>Lists IPv4 network adapters. A read-only query: no socket is opened and nothing is sent.</summary>
public static class NetworkAdapterDiscovery
{
    public static readonly NetworkAdapterOption AnyAdapter = new(string.Empty, "Automatic (Windows chooses)", null);

    public static IReadOnlyList<NetworkAdapterOption> GetIPv4Adapters()
    {
        var result = new List<NetworkAdapterOption> { AnyAdapter };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var address = unicast.Address.ToString();
                    result.Add(new NetworkAdapterOption(address, $"{nic.Name} — {address}", Broadcast(unicast)));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Only "Automatic" is offered; sending still works through the default route.
        }

        return result;
    }

    private static string? Broadcast(UnicastIPAddressInformation unicast)
    {
        var mask = unicast.IPv4Mask;
        if (mask is null || mask.Equals(IPAddress.Any))
        {
            return null;
        }

        var address = unicast.Address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            broadcast[i] = (byte)(address[i] | ~maskBytes[i]);
        }

        return new IPAddress(broadcast).ToString();
    }
}
