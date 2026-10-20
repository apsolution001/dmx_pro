using DmxPro.Core.Validation;
using DmxPro.Output.Protocols;

namespace DmxPro.Output.Configuration;

/// <summary>
/// Art-Net (ArtDmx over UDP). Project universe <c>u</c> is sent to Port-Address
/// <c>(Net·256 + SubNet·16 + Universe) + (u − 1)</c>, so with the defaults project universe 1 → Port-Address 0.
/// </summary>
public sealed class ArtNetOutputConfig : OutputConfig
{
    public const string LimitedBroadcast = "255.255.255.255";

    /// <summary>Unicast node IP or a broadcast address (e.g. <c>192.168.1.255</c> or <c>2.255.255.255</c>).</summary>
    public string Destination { get; set; } = LimitedBroadcast;

    /// <summary>IPv4 address of the network adapter to send from; empty = let Windows choose.</summary>
    public string? BindAddress { get; set; }

    /// <summary>Art-Net Net of the first universe (0–127).</summary>
    public int Net { get; set; }

    /// <summary>Art-Net Sub-Net of the first universe (0–15).</summary>
    public int SubNet { get; set; }

    /// <summary>Art-Net Universe (low nibble) of the first universe (0–15).</summary>
    public int Universe { get; set; }

    /// <summary>Destination UDP port; 6454 for real nodes (configurable for tests).</summary>
    public int Port { get; set; } = ArtNetPacket.DefaultPort;

    public override OutputKind Kind => OutputKind.ArtNet;

    protected override string DefaultName => "Art-Net";

    protected override void ValidateCore(List<ValidationIssue> issues)
    {
        if (!NetworkAddress.TryParseIPv4(Destination, out _))
        {
            issues.Add(Error("ARTNET_DESTINATION_INVALID", $"'{Destination}' is not a valid IPv4 address."));
        }

        if (!NetworkAddress.TryParseBind(BindAddress, out _))
        {
            issues.Add(Error("OUTPUT_BIND_ADDRESS_INVALID", $"adapter address '{BindAddress}' is not a valid IPv4 address."));
        }

        if (Port is < 1 or > 65535)
        {
            issues.Add(Error("OUTPUT_PORT_RANGE", $"UDP port {Port} is out of range."));
        }

        var rangeOk = true;
        if (Net is < 0 or > ArtNetPacket.MaxNet)
        {
            issues.Add(Error("ARTNET_NET_RANGE", $"Net {Net} is out of range (0–{ArtNetPacket.MaxNet})."));
            rangeOk = false;
        }

        if (SubNet is < 0 or > 15)
        {
            issues.Add(Error("ARTNET_SUBNET_RANGE", $"Sub-Net {SubNet} is out of range (0–15)."));
            rangeOk = false;
        }

        if (Universe is < 0 or > 15)
        {
            issues.Add(Error("ARTNET_UNIVERSE_RANGE", $"Art-Net universe {Universe} is out of range (0–15)."));
            rangeOk = false;
        }

        if (!rangeOk)
        {
            return;
        }

        foreach (var universe in GetValidUniverses())
        {
            if (!UniverseMapping.TryGetArtNetPortAddress(universe, Net, SubNet, Universe, out _))
            {
                issues.Add(Error(
                    "ARTNET_PORT_ADDRESS_RANGE",
                    $"universe {universe} maps beyond the highest Art-Net Port-Address ({ArtNetPacket.MaxPortAddress})."));
            }
        }
    }
}
