using DmxPro.Output.Protocols;

namespace DmxPro.Output.Tests;

public class ArtNetPacketTests
{
    [Fact]
    public void WriteArtDmx_FullUniverse_WritesExactHeaderAndData()
    {
        var data = TestFrames.Ramp(512);
        var packet = new byte[ArtNetPacket.MaxPacketLength];

        var length = ArtNetPacket.WriteArtDmx(packet, sequence: 5, physical: 2, portAddress: 0, data);

        Assert.Equal(530, length);
        byte[] expectedHeader =
        [
            0x41, 0x72, 0x74, 0x2D, 0x4E, 0x65, 0x74, 0x00, // "Art-Net\0"
            0x00, 0x50,                                     // OpCode 0x5000 little-endian
            0x00, 0x0E,                                     // ProtVer 14 big-endian
            0x05,                                           // Sequence
            0x02,                                           // Physical
            0x00, 0x00,                                     // SubUni, Net
            0x02, 0x00,                                     // Length 512 big-endian
        ];
        Assert.Equal(expectedHeader, packet[..18]);
        Assert.Equal(data, packet[18..530]);
    }

    [Theory]
    [InlineData(0x0000, 0x00, 0x00)]
    [InlineData(0x0001, 0x01, 0x00)]
    [InlineData(0x0010, 0x10, 0x00)]
    [InlineData(0x1234, 0x34, 0x12)]
    [InlineData(0x7FFF, 0xFF, 0x7F)]
    public void WriteArtDmx_PortAddress_SplitsIntoSubUniAndNet(int portAddress, byte subUni, byte net)
    {
        var packet = new byte[ArtNetPacket.MaxPacketLength];

        ArtNetPacket.WriteArtDmx(packet, 1, 0, (ushort)portAddress, TestFrames.Ramp(2));

        Assert.Equal(subUni, packet[14]);
        Assert.Equal(net, packet[15]);
    }

    [Fact]
    public void WriteArtDmx_OddLength_PadsToEvenWithZero()
    {
        var packet = Enumerable.Repeat((byte)0xEE, ArtNetPacket.MaxPacketLength).ToArray();

        var length = ArtNetPacket.WriteArtDmx(packet, 1, 0, 0, [10, 20, 30]);

        Assert.Equal(22, length);
        Assert.Equal(new byte[] { 0x00, 0x04 }, packet[16..18]);
        Assert.Equal(new byte[] { 10, 20, 30, 0 }, packet[18..22]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WriteArtDmx_ShorterThanTwo_UsesMinimumLengthTwo(int slots)
    {
        var packet = Enumerable.Repeat((byte)0xEE, ArtNetPacket.MaxPacketLength).ToArray();

        var length = ArtNetPacket.WriteArtDmx(packet, 1, 0, 0, Enumerable.Repeat((byte)7, slots).ToArray());

        Assert.Equal(20, length);
        Assert.Equal(new byte[] { 0x00, 0x02 }, packet[16..18]);
        Assert.Equal(slots == 1 ? new byte[] { 7, 0 } : new byte[] { 0, 0 }, packet[18..20]);
    }

    [Fact]
    public void WriteArtDmx_MoreThan512Values_SendsOnly512()
    {
        var packet = new byte[ArtNetPacket.MaxPacketLength];

        var length = ArtNetPacket.WriteArtDmx(packet, 1, 0, 0, new byte[600]);

        Assert.Equal(530, length);
    }

    [Fact]
    public void WriteArtDmx_PortAddressAbove15Bits_Throws()
    {
        var packet = new byte[ArtNetPacket.MaxPacketLength];

        Assert.Throws<ArgumentOutOfRangeException>(() => ArtNetPacket.WriteArtDmx(packet, 1, 0, 0x8000, [1, 2]));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(254, 255)]
    [InlineData(255, 1)]
    public void NextSequence_Rolls1To255_SkipsZero(int current, int expected)
    {
        Assert.Equal((byte)expected, ArtNetPacket.NextSequence((byte)current));
    }

    [Fact]
    public void WriteArtDmx_Repeated_AllocatesNothing()
    {
        var packet = new byte[ArtNetPacket.MaxPacketLength];
        var data = TestFrames.Ramp(512);
        ArtNetPacket.WriteArtDmx(packet, 1, 0, 1, data);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ArtNetPacket.WriteArtDmx(packet, (byte)i, 0, 1, data);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
