using System.Net;
using DmxPro.Output.Protocols;

namespace DmxPro.Output.Tests;

public class SacnPacketTests
{
    private static readonly Guid TestCid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    [Fact]
    public void WriteDataPacket_FullUniverse_MatchesE131LayoutByteForByte()
    {
        var name = new byte[SacnPacket.SourceNameLength];
        SacnPacket.EncodeSourceName("DMX Pro", name);
        var data = TestFrames.Ramp(512);
        var packet = new byte[SacnPacket.MaxPacketLength];

        var length = SacnPacket.WriteDataPacket(packet, TestCid, name, 100, 0, 0x2A, SacnOptions.None, 1, data);

        var expected = new List<byte>();
        // Root layer
        expected.AddRange([0x00, 0x10]); // preamble size
        expected.AddRange([0x00, 0x00]); // postamble size
        expected.AddRange([0x41, 0x53, 0x43, 0x2D, 0x45, 0x31, 0x2E, 0x31, 0x37, 0x00, 0x00, 0x00]); // "ASC-E1.17"
        expected.AddRange([0x72, 0x6E]); // flags 0x7 | length 622 (638 − 16)
        expected.AddRange([0x00, 0x00, 0x00, 0x04]); // VECTOR_ROOT_E131_DATA
        expected.AddRange([0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF]); // CID
        // Framing layer
        expected.AddRange([0x72, 0x58]); // flags | length 600 (638 − 38)
        expected.AddRange([0x00, 0x00, 0x00, 0x02]); // VECTOR_E131_DATA_PACKET
        expected.AddRange("DMX Pro"u8.ToArray());
        expected.AddRange(new byte[64 - 7]); // null padding
        expected.Add(100); // priority
        expected.AddRange([0x00, 0x00]); // synchronization address
        expected.Add(0x2A); // sequence
        expected.Add(0x00); // options
        expected.AddRange([0x00, 0x01]); // universe 1
        // DMP layer
        expected.AddRange([0x72, 0x0B]); // flags | length 523 (638 − 115)
        expected.Add(0x02); // VECTOR_DMP_SET_PROPERTY
        expected.Add(0xA1); // address & data type
        expected.AddRange([0x00, 0x00]); // first property address
        expected.AddRange([0x00, 0x01]); // address increment
        expected.AddRange([0x02, 0x01]); // property value count 513
        expected.Add(0x00); // start code
        expected.AddRange(data);

        Assert.Equal(638, length);
        Assert.Equal(expected.ToArray(), packet[..length]);
    }

    [Fact]
    public void WriteDataPacket_ThreeSlots_AdjustsAllLengthFields()
    {
        var name = new byte[SacnPacket.SourceNameLength];
        var packet = new byte[SacnPacket.MaxPacketLength];

        var length = SacnPacket.WriteDataPacket(packet, TestCid, name, 100, 0, 0, SacnOptions.None, 1, [9, 8, 7]);

        Assert.Equal(129, length);
        Assert.Equal(new byte[] { 0x70, 0x71 }, packet[16..18]);   // 113
        Assert.Equal(new byte[] { 0x70, 0x5B }, packet[38..40]);   // 91
        Assert.Equal(new byte[] { 0x70, 0x0E }, packet[115..117]); // 14
        Assert.Equal(new byte[] { 0x00, 0x04 }, packet[123..125]); // start code + 3
        Assert.Equal(new byte[] { 0, 9, 8, 7 }, packet[125..129]);
    }

    [Fact]
    public void WriteDataPacket_StreamTerminatedAndHighUniverse_WritesOptionAndUniverse()
    {
        var name = new byte[SacnPacket.SourceNameLength];
        var packet = new byte[SacnPacket.MaxPacketLength];

        SacnPacket.WriteDataPacket(packet, TestCid, name, 200, 0x1234, 255, SacnOptions.StreamTerminated, 63999, [1]);

        Assert.Equal(200, packet[108]);
        Assert.Equal(new byte[] { 0x12, 0x34 }, packet[109..111]);
        Assert.Equal(255, packet[111]);
        Assert.Equal(0x40, packet[112]);
        Assert.Equal(new byte[] { 0xF9, 0xFF }, packet[113..115]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64000)]
    public void WriteDataPacket_UniverseOutOfRange_Throws(int universe)
    {
        var name = new byte[SacnPacket.SourceNameLength];
        var packet = new byte[SacnPacket.MaxPacketLength];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SacnPacket.WriteDataPacket(packet, TestCid, name, 100, 0, 0, SacnOptions.None, (ushort)universe, [1]));
    }

    [Fact]
    public void WriteDataPacket_PriorityAbove200_Throws()
    {
        var name = new byte[SacnPacket.SourceNameLength];
        var packet = new byte[SacnPacket.MaxPacketLength];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SacnPacket.WriteDataPacket(packet, TestCid, name, 201, 0, 0, SacnOptions.None, 1, [1]));
    }

    [Fact]
    public void EncodeSourceName_TooLong_TruncatesTo63BytesAndNullTerminates()
    {
        var name = TestFrames.Filled(0xFF, SacnPacket.SourceNameLength);

        SacnPacket.EncodeSourceName(new string('a', 70), name);

        Assert.All(name[..63], b => Assert.Equal((byte)'a', b));
        Assert.Equal(0, name[63]);
    }

    [Fact]
    public void EncodeSourceName_MultiByteCharacters_CutsOnCharacterBoundary()
    {
        var name = new byte[SacnPacket.SourceNameLength];

        SacnPacket.EncodeSourceName(new string('é', 40), name); // 2 bytes each → 80 bytes

        Assert.Equal(new string('é', 31), System.Text.Encoding.UTF8.GetString(name, 0, 62));
        Assert.Equal(0, name[62]);
        Assert.Equal(0, name[63]);
    }

    [Theory]
    [InlineData(1, "239.255.0.1")]
    [InlineData(256, "239.255.1.0")]
    [InlineData(63999, "239.255.249.255")]
    public void GetMulticastAddress_Universe_Is239_255_HiLo(int universe, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), SacnPacket.GetMulticastAddress((ushort)universe));
    }

    [Fact]
    public void NextSequence_255_WrapsToZero()
    {
        Assert.Equal(0, SacnPacket.NextSequence(255));
        Assert.Equal(1, SacnPacket.NextSequence(0));
    }

    [Fact]
    public void WriteDataPacket_Repeated_AllocatesNothing()
    {
        var name = new byte[SacnPacket.SourceNameLength];
        var packet = new byte[SacnPacket.MaxPacketLength];
        var data = TestFrames.Ramp(512);
        SacnPacket.WriteDataPacket(packet, TestCid, name, 100, 0, 0, SacnOptions.None, 1, data);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            SacnPacket.WriteDataPacket(packet, TestCid, name, 100, 0, (byte)i, SacnOptions.None, 1, data);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
