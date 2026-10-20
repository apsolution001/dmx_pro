namespace DmxPro.Output.Protocols;

/// <summary>
/// Enttec DMX USB Pro message writer: <c>0x7E · Label · Length LSB · Length MSB · Data · 0xE7</c>.
/// Pure and allocation-free.
/// </summary>
public static class EnttecProPacket
{
    public const byte StartOfMessage = 0x7E;
    public const byte EndOfMessage = 0xE7;

    /// <summary>Label 6: Output Only Send DMX Packet Request (data = start code + slots).</summary>
    public const byte LabelSendDmx = 6;

    public const int MaxSlots = 512;

    /// <summary>The widget requires at least 24 slots (DMX512 minimum frame); shorter frames are zero-padded.</summary>
    public const int MinSlots = 24;

    /// <summary>Header (4) + start code (1) + 512 slots + end marker (1).</summary>
    public const int MaxPacketLength = 4 + 1 + MaxSlots + 1;

    /// <summary>Total message size for <paramref name="slotCount"/> slots.</summary>
    public static int GetPacketLength(int slotCount) => 4 + 1 + Math.Clamp(slotCount, MinSlots, MaxSlots) + 1;

    /// <summary>Writes a label-6 "send DMX" message. Values beyond 512 are ignored.</summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteSendDmx(Span<byte> destination, ReadOnlySpan<byte> data)
    {
        var slots = data.Length > MaxSlots ? data[..MaxSlots] : data;
        var slotCount = Math.Max(slots.Length, MinSlots);
        var total = 4 + 1 + slotCount + 1;
        if (destination.Length < total)
        {
            throw new ArgumentException($"Destination needs {total} bytes.", nameof(destination));
        }

        var payloadLength = slotCount + 1; // start code + slots
        destination[0] = StartOfMessage;
        destination[1] = LabelSendDmx;
        destination[2] = (byte)(payloadLength & 0xFF);
        destination[3] = (byte)(payloadLength >> 8);
        destination[4] = 0x00; // DMX start code
        slots.CopyTo(destination[5..]);
        destination.Slice(5 + slots.Length, slotCount - slots.Length).Clear();
        destination[total - 1] = EndOfMessage;
        return total;
    }
}
