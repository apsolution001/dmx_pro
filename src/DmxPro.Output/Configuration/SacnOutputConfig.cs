using System.Text;
using DmxPro.Core.Validation;
using DmxPro.Output.Protocols;

namespace DmxPro.Output.Configuration;

/// <summary>
/// sACN / ANSI E1.31. Project universe <c>u</c> is sent as sACN universe <c>u + UniverseOffset</c> (must be 1–63999),
/// by multicast to <c>239.255.{hi}.{lo}</c> or by unicast to <see cref="UnicastAddress"/>.
/// </summary>
public sealed class SacnOutputConfig : OutputConfig
{
    public const string DefaultSourceName = "DMX Pro";

    public bool UseMulticast { get; set; } = true;

    /// <summary>Receiver IP when <see cref="UseMulticast"/> is false.</summary>
    public string? UnicastAddress { get; set; }

    /// <summary>IPv4 address of the network adapter to send from; empty = let Windows choose.</summary>
    public string? BindAddress { get; set; }

    /// <summary>Source priority 0–200 (receivers follow the highest-priority source).</summary>
    public int Priority { get; set; } = SacnPacket.DefaultPriority;

    /// <summary>Human-readable source name shown by receivers (max 63 UTF-8 bytes on the wire).</summary>
    public string SourceName { get; set; } = DefaultSourceName;

    /// <summary>
    /// Component identifier. Must stay stable across sessions so receivers recognise this source; the app should set
    /// its per-install CID here.
    /// </summary>
    public Guid Cid { get; set; } = Guid.NewGuid();

    /// <summary>Added to the project universe to get the sACN universe.</summary>
    public int UniverseOffset { get; set; }

    /// <summary>Destination UDP port; 5568 for real receivers (configurable for tests).</summary>
    public int Port { get; set; } = SacnPacket.DefaultPort;

    public override OutputKind Kind => OutputKind.Sacn;

    protected override string DefaultName => "sACN";

    protected override void ValidateCore(List<ValidationIssue> issues)
    {
        if (!UseMulticast && !NetworkAddress.TryParseIPv4(UnicastAddress, out _))
        {
            issues.Add(Error("SACN_UNICAST_ADDRESS_INVALID", $"receiver address '{UnicastAddress}' is not a valid IPv4 address."));
        }

        if (!NetworkAddress.TryParseBind(BindAddress, out _))
        {
            issues.Add(Error("OUTPUT_BIND_ADDRESS_INVALID", $"adapter address '{BindAddress}' is not a valid IPv4 address."));
        }

        if (Port is < 1 or > 65535)
        {
            issues.Add(Error("OUTPUT_PORT_RANGE", $"UDP port {Port} is out of range."));
        }

        if (Priority is < 0 or > SacnPacket.MaxPriority)
        {
            issues.Add(Error("SACN_PRIORITY_RANGE", $"priority {Priority} is out of range (0–{SacnPacket.MaxPriority})."));
        }

        if (Cid == Guid.Empty)
        {
            issues.Add(Error("SACN_CID_EMPTY", "the source identifier (CID) is missing."));
        }

        if (string.IsNullOrWhiteSpace(SourceName))
        {
            issues.Add(Error("SACN_SOURCE_NAME_EMPTY", "enter a source name."));
        }
        else if (Encoding.UTF8.GetByteCount(SourceName) > SacnPacket.SourceNameLength - 1)
        {
            issues.Add(Warning("SACN_SOURCE_NAME_TRUNCATED", "the source name is longer than 63 bytes and will be shortened."));
        }

        foreach (var universe in GetValidUniverses())
        {
            if (!UniverseMapping.TryGetSacnUniverse(universe, UniverseOffset, out _))
            {
                issues.Add(Error(
                    "SACN_UNIVERSE_RANGE",
                    $"universe {universe} with offset {UniverseOffset} maps outside sACN universes " +
                    $"{UniverseMapping.SacnMinUniverse}–{UniverseMapping.SacnMaxUniverse}."));
            }
        }
    }
}
