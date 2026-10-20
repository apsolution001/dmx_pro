using System.Net;
using System.Net.Sockets;
using DmxPro.Output.Configuration;
using DmxPro.Output.Protocols;
using DmxPro.Output.Transport;

namespace DmxPro.Output;

/// <summary>Art-Net output: one ArtDmx packet per universe per frame, unicast or broadcast over UDP.</summary>
public sealed class ArtNetOutput : DmxOutputBase<ArtNetOutputConfig>
{
    private readonly byte[] _packet = new byte[ArtNetPacket.MaxPacketLength];
    private ushort[] _portAddresses = [];
    private byte[] _sequences = [];
    private IPAddress _bindAddress = IPAddress.Any;
    private SocketAddress? _destination;
    private Socket? _socket;

    public ArtNetOutput()
        : base("Art-Net")
    {
    }

    protected override void Prepare(ArtNetOutputConfig config, IReadOnlyList<int> universes)
    {
        var portAddresses = new ushort[universes.Count];
        for (var i = 0; i < universes.Count; i++)
        {
            if (!UniverseMapping.TryGetArtNetPortAddress(universes[i], config.Net, config.SubNet, config.Universe, out portAddresses[i]))
            {
                throw new OutputTransportException($"Universe {universes[i]} has no Art-Net Port-Address.");
            }
        }

        if (!NetworkAddress.TryParseIPv4(config.Destination, out var destination)
            || !NetworkAddress.TryParseBind(config.BindAddress, out var bind))
        {
            throw new OutputTransportException("Invalid Art-Net address.");
        }

        _portAddresses = portAddresses;
        _sequences = new byte[universes.Count];
        _bindAddress = bind;
        _destination = new IPEndPoint(destination, config.Port).Serialize();
    }

    protected override void OpenTransport() =>
        _socket = UdpSocketFactory.Create(_bindAddress, enableBroadcast: true); // broadcast destinations need SO_BROADCAST

    protected override void SendUniverse(int slotIndex, ReadOnlySpan<byte> data)
    {
        var sequence = ArtNetPacket.NextSequence(_sequences[slotIndex]);
        _sequences[slotIndex] = sequence;
        var length = ArtNetPacket.WriteArtDmx(_packet, sequence, physical: 0, _portAddresses[slotIndex], data);
        _socket!.SendTo(_packet.AsSpan(0, length), SocketFlags.None, _destination!);
    }

    protected override void CloseTransport()
    {
        _socket?.Dispose();
        _socket = null;
    }

}
