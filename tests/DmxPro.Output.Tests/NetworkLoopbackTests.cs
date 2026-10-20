using System.Net;
using System.Net.Sockets;
using DmxPro.Output.Configuration;

namespace DmxPro.Output.Tests;

/// <summary>Sends real UDP packets to 127.0.0.1 on an ephemeral port (no network hardware needed).</summary>
public class NetworkLoopbackTests
{
    [Fact]
    public async Task ArtNet_Unicast_DeliversArtDmxPerUniverse()
    {
        using var receiver = CreateReceiver(out var port);
        await using var output = new ArtNetOutput();
        await output.StartAsync(new ArtNetOutputConfig
        {
            Destination = "127.0.0.1",
            BindAddress = "127.0.0.1",
            Port = port,
            Universe = 3,
            Universes = [1, 2],
        });
        Assert.Equal(OutputState.Sending, output.Status.State);

        output.Submit(1, TestFrames.Filled(0x11));
        output.Submit(2, TestFrames.Filled(0x22, 3));

        var seen = new Dictionary<int, byte[]>();
        await ReceiveUntilAsync(receiver, packet =>
        {
            seen[packet[14] | (packet[15] << 8)] = packet;
            return seen.Count == 2;
        });

        var first = seen[3];
        Assert.Equal("Art-Net\0"u8.ToArray(), first[..8]);
        Assert.Equal(new byte[] { 0x00, 0x50, 0x00, 0x0E }, first[8..12]);
        Assert.NotEqual(0, first[12]); // sequence enabled
        Assert.Equal(530, first.Length);
        Assert.All(first[18..], b => Assert.Equal(0x11, b));

        var second = seen[4];
        Assert.Equal(22, second.Length); // 3 slots padded to 4
        Assert.Equal(new byte[] { 0x22, 0x22, 0x22, 0x00 }, second[18..]);
    }

    [Fact]
    public async Task Sacn_UnicastThenStop_SendsDataAndThreeTerminatedPackets()
    {
        using var receiver = CreateReceiver(out var port);
        var cid = Guid.NewGuid();
        var output = new SacnOutput();
        await output.StartAsync(new SacnOutputConfig
        {
            UseMulticast = false,
            UnicastAddress = "127.0.0.1",
            BindAddress = "127.0.0.1",
            Port = port,
            Cid = cid,
            Priority = 120,
            UniverseOffset = 9,
        });
        Assert.Equal(OutputState.Sending, output.Status.State);

        output.Submit(1, TestFrames.Filled(0x7F));
        byte[]? data = null;
        await ReceiveUntilAsync(receiver, p => (data = p) is not null);

        Assert.Equal(638, data!.Length);
        Assert.Equal(cid.ToByteArray(bigEndian: true), data[22..38]);
        Assert.Equal(120, data[108]);
        Assert.Equal(new byte[] { 0x00, 0x0A }, data[113..115]); // project universe 1 + offset 9
        Assert.Equal(0x7F, data[126]);

        await output.DisposeAsync();
        var terminated = 0;
        await ReceiveUntilAsync(receiver, p =>
        {
            if ((p[112] & 0x40) != 0)
            {
                terminated++;
            }

            return terminated == 3;
        });
        Assert.Equal(3, terminated);
    }

    [Fact]
    public async Task ArtNet_BindAddressNotOnThisPc_ReportsErrorWithoutThrowing()
    {
        await using var output = new ArtNetOutput { RetryDelay = TimeSpan.FromMilliseconds(50) };

        await output.StartAsync(new ArtNetOutputConfig { BindAddress = "203.0.113.77" });
        output.Submit(1, TestFrames.Filled(1));

        Assert.Equal(OutputState.Error, output.Status.State);
        Assert.Contains("adapter", output.Status.Message);
    }

    private static Socket CreateReceiver(out int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        return socket;
    }

    private static async Task ReceiveUntilAsync(Socket socket, Func<byte[], bool> done, int timeoutMilliseconds = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMilliseconds);
        var buffer = new byte[1024];
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
            if (done(buffer[..received]))
            {
                return;
            }
        }
    }
}
