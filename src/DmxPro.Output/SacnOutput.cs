using System.Net;
using System.Net.Sockets;
using DmxPro.Output.Configuration;
using DmxPro.Output.Protocols;
using DmxPro.Output.Transport;

namespace DmxPro.Output;

/// <summary>
/// sACN (E1.31) output: multicast to 239.255.{hi}.{lo} or unicast. On stop it sends three Stream_Terminated packets
/// per universe so receivers release the source immediately instead of waiting for the 2.5 s timeout.
/// </summary>
public sealed class SacnOutput : DmxOutputBase<SacnOutputConfig>
{
    /// <summary>E1.31 §6.7.1: send the terminated packet three times.</summary>
    public const int TerminationPacketCount = 3;

    private readonly byte[] _packet = new byte[SacnPacket.MaxPacketLength];
    private readonly byte[] _sourceName = new byte[SacnPacket.SourceNameLength];
    private ushort[] _sacnUniverses = [];
    private byte[] _sequences = [];
    private SocketAddress[] _destinations = [];
    private IPAddress _bindAddress = IPAddress.Any;
    private bool _multicast;
    private Guid _cid;
    private byte _priority;
    private Socket? _socket;

    public SacnOutput()
        : base("sACN")
    {
    }

    protected override void Prepare(SacnOutputConfig config, IReadOnlyList<int> universes)
    {
        if (!NetworkAddress.TryParseBind(config.BindAddress, out var bind))
        {
            throw new OutputTransportException("Invalid sACN adapter address.");
        }

        IPAddress? unicast = null;
        if (!config.UseMulticast && !NetworkAddress.TryParseIPv4(config.UnicastAddress, out unicast))
        {
            throw new OutputTransportException("Invalid sACN receiver address.");
        }

        var sacnUniverses = new ushort[universes.Count];
        var destinations = new SocketAddress[universes.Count];
        for (var i = 0; i < universes.Count; i++)
        {
            if (!UniverseMapping.TryGetSacnUniverse(universes[i], config.UniverseOffset, out sacnUniverses[i]))
            {
                throw new OutputTransportException($"Universe {universes[i]} has no sACN universe.");
            }

            var target = unicast ?? SacnPacket.GetMulticastAddress(sacnUniverses[i]);
            destinations[i] = new IPEndPoint(target, config.Port).Serialize();
        }

        SacnPacket.EncodeSourceName(config.SourceName, _sourceName);
        _sacnUniverses = sacnUniverses;
        _destinations = destinations;
        _sequences = new byte[universes.Count];
        _bindAddress = bind;
        _multicast = config.UseMulticast;
        _cid = config.Cid;
        _priority = (byte)config.Priority;
    }

    protected override void OpenTransport()
    {
        var socket = UdpSocketFactory.Create(_bindAddress, enableBroadcast: false);
        try
        {
            if (_multicast)
            {
                if (!_bindAddress.Equals(IPAddress.Any))
                {
                    // Send multicast out of the adapter the user picked, not whatever the routing table prefers.
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, _bindAddress.GetAddressBytes());
                }

                // Lets sACN viewers on this PC see the output.
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
            }

            _socket = socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    protected override void SendUniverse(int slotIndex, ReadOnlySpan<byte> data) =>
        Send(slotIndex, data, SacnOptions.None);

    protected override void BeforeClose()
    {
        for (var i = 0; i < _sacnUniverses.Length; i++)
        {
            if (!TryGetLastSent(i, out var data))
            {
                continue;
            }

            for (var n = 0; n < TerminationPacketCount; n++)
            {
                Send(i, data, SacnOptions.StreamTerminated);
            }
        }
    }

    protected override void CloseTransport()
    {
        _socket?.Dispose();
        _socket = null;
    }

    private void Send(int slotIndex, ReadOnlySpan<byte> data, SacnOptions options)
    {
        var sequence = SacnPacket.NextSequence(_sequences[slotIndex]);
        _sequences[slotIndex] = sequence;
        var length = SacnPacket.WriteDataPacket(
            _packet,
            _cid,
            _sourceName,
            _priority,
            synchronizationAddress: 0,
            sequence,
            options,
            _sacnUniverses[slotIndex],
            data);
        _socket!.SendTo(_packet.AsSpan(0, length), SocketFlags.None, _destinations[slotIndex]);
    }
}
