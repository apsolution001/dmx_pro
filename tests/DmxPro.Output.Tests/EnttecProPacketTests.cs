using DmxPro.Output.Protocols;

namespace DmxPro.Output.Tests;

public class EnttecProPacketTests
{
    [Fact]
    public void WriteSendDmx_FullUniverse_FramesWithLabel6AndEndMarker()
    {
        var data = TestFrames.Ramp(512);
        var packet = new byte[EnttecProPacket.MaxPacketLength];

        var length = EnttecProPacket.WriteSendDmx(packet, data);

        Assert.Equal(518, length);
        Assert.Equal(new byte[] { 0x7E, 0x06, 0x01, 0x02, 0x00 }, packet[..5]); // length 513 = 0x0201, start code 0
        Assert.Equal(data, packet[5..517]);
        Assert.Equal(0xE7, packet[517]);
    }

    [Fact]
    public void WriteSendDmx_ShortFrame_PadsToMinimum24Slots()
    {
        var packet = TestFrames.Filled(0xEE, EnttecProPacket.MaxPacketLength);

        var length = EnttecProPacket.WriteSendDmx(packet, [1, 2, 3]);

        var expected = new List<byte> { 0x7E, 0x06, 0x19, 0x00, 0x00, 1, 2, 3 }; // 25 = start code + 24 slots
        expected.AddRange(new byte[21]);
        expected.Add(0xE7);
        Assert.Equal(30, length);
        Assert.Equal(expected.ToArray(), packet[..length]);
    }

    [Fact]
    public void WriteSendDmx_Repeated_AllocatesNothing()
    {
        var packet = new byte[EnttecProPacket.MaxPacketLength];
        var data = TestFrames.Ramp(512);
        EnttecProPacket.WriteSendDmx(packet, data);

        Assert.Equal(0, AllocationProbe.Measure(_ => EnttecProPacket.WriteSendDmx(packet, data)));
    }
}
