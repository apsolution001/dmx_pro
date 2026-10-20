using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace DmxPro.Output.Protocols;

/// <summary>
/// ANSI E1.31 (sACN) data packet writer. Pure and allocation-free: writes into a caller-provided buffer.
/// </summary>
/// <remarks>
/// Root layer (offset 0) → Framing layer (offset 38) → DMP layer (offset 115) → start code (125) → slots (126…).
/// A full universe is 638 bytes. Every PDU starts with flags (0x7) in the top 4 bits and its own length (from the
/// start of that PDU to the end of the packet) in the low 12 bits.
/// </remarks>
public static class SacnPacket
{
    public const int DefaultPort = 5568;
    public const int DefaultPriority = 100;
    public const int MaxPriority = 200;
    public const int SourceNameLength = 64;
    public const int MaxSlots = 512;

    public const int RootLayerOffset = 0;
    public const int FramingLayerOffset = 38;
    public const int DmpLayerOffset = 115;
    public const int StartCodeOffset = 125;
    public const int HeaderLength = 126;
    public const int MaxPacketLength = HeaderLength + MaxSlots;

    public const uint VectorRootE131Data = 0x00000004;
    public const uint VectorE131DataPacket = 0x00000002;
    public const byte VectorDmpSetProperty = 0x02;

    internal const ushort PduFlags = 0x7000;

    /// <summary>ACN packet identifier "ASC-E1.17" padded with zeros to 12 bytes.</summary>
    private static ReadOnlySpan<byte> AcnPacketIdentifier =>
        [0x41, 0x53, 0x43, 0x2D, 0x45, 0x31, 0x2E, 0x31, 0x37, 0x00, 0x00, 0x00];

    /// <summary>Total packet size for <paramref name="slotCount"/> slots (clamped to 512).</summary>
    public static int GetPacketLength(int slotCount) => HeaderLength + Math.Clamp(slotCount, 0, MaxSlots);

    /// <summary>Next sequence number (0–255 wrapping; E1.31 uses all values).</summary>
    public static byte NextSequence(byte current) => unchecked((byte)(current + 1));

    /// <summary>Multicast group for <paramref name="universe"/>: <c>239.255.{hi}.{lo}</c>.</summary>
    public static IPAddress GetMulticastAddress(ushort universe) =>
        new([239, 255, (byte)(universe >> 8), (byte)(universe & 0xFF)]);

    /// <summary>
    /// Encodes <paramref name="sourceName"/> as UTF-8 into a 64-byte field: truncated on a character boundary to at
    /// most 63 bytes and null-padded (the field is always null-terminated).
    /// </summary>
    public static void EncodeSourceName(string sourceName, Span<byte> destination)
    {
        if (destination.Length < SourceNameLength)
        {
            throw new ArgumentException($"Destination needs {SourceNameLength} bytes.", nameof(destination));
        }

        var field = destination[..SourceNameLength];
        field.Clear();
        var text = sourceName.AsSpan();
        while (Encoding.UTF8.GetByteCount(text) > SourceNameLength - 1)
        {
            text = text[..^(text.Length > 1 && char.IsLowSurrogate(text[^1]) ? 2 : 1)];
        }

        Encoding.UTF8.GetBytes(text, field);
    }

    /// <summary>Writes an E1.31 data packet.</summary>
    /// <param name="destination">Output buffer, at least <see cref="GetPacketLength"/> bytes.</param>
    /// <param name="cid">Component identifier, written in RFC 4122 (network) byte order.</param>
    /// <param name="sourceName">64-byte field from <see cref="EncodeSourceName"/>.</param>
    /// <param name="priority">0–200.</param>
    /// <param name="synchronizationAddress">0 = not synchronized.</param>
    /// <param name="sequence">Per-universe sequence number.</param>
    /// <param name="options">Option bits.</param>
    /// <param name="universe">sACN universe 1–63999.</param>
    /// <param name="data">Slot values (start code excluded); values beyond 512 are ignored.</param>
    /// <returns>Number of bytes written.</returns>
    public static int WriteDataPacket(
        Span<byte> destination,
        Guid cid,
        ReadOnlySpan<byte> sourceName,
        byte priority,
        ushort synchronizationAddress,
        byte sequence,
        SacnOptions options,
        ushort universe,
        ReadOnlySpan<byte> data)
    {
        if (sourceName.Length != SourceNameLength)
        {
            throw new ArgumentException($"Source name field must be {SourceNameLength} bytes.", nameof(sourceName));
        }

        if (priority > MaxPriority)
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority is 0–200.");
        }

        if (universe is < UniverseMapping.SacnMinUniverse or > UniverseMapping.SacnMaxUniverse)
        {
            throw new ArgumentOutOfRangeException(nameof(universe), universe, "sACN universe is 1–63999.");
        }

        var slots = data.Length > MaxSlots ? data[..MaxSlots] : data;
        var total = HeaderLength + slots.Length;
        if (destination.Length < total)
        {
            throw new ArgumentException($"Destination needs {total} bytes.", nameof(destination));
        }

        // Root layer
        BinaryPrimitives.WriteUInt16BigEndian(destination[0..], 0x0010); // preamble size
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 0x0000); // postamble size
        AcnPacketIdentifier.CopyTo(destination[4..]);
        WriteFlagsAndLength(destination[16..], total - 16);
        BinaryPrimitives.WriteUInt32BigEndian(destination[18..], VectorRootE131Data);
        cid.TryWriteBytes(destination.Slice(22, 16), bigEndian: true, out _);

        // Framing layer
        WriteFlagsAndLength(destination[FramingLayerOffset..], total - FramingLayerOffset);
        BinaryPrimitives.WriteUInt32BigEndian(destination[40..], VectorE131DataPacket);
        sourceName.CopyTo(destination[44..]);
        destination[107] = 0; // the name field is always null-terminated
        destination[108] = priority;
        BinaryPrimitives.WriteUInt16BigEndian(destination[109..], synchronizationAddress);
        destination[111] = sequence;
        destination[112] = (byte)options;
        BinaryPrimitives.WriteUInt16BigEndian(destination[113..], universe);

        // DMP layer
        WriteFlagsAndLength(destination[DmpLayerOffset..], total - DmpLayerOffset);
        destination[117] = VectorDmpSetProperty;
        destination[118] = 0xA1; // address type & data type
        BinaryPrimitives.WriteUInt16BigEndian(destination[119..], 0x0000); // first property address
        BinaryPrimitives.WriteUInt16BigEndian(destination[121..], 0x0001); // address increment
        BinaryPrimitives.WriteUInt16BigEndian(destination[123..], (ushort)(slots.Length + 1)); // + start code
        destination[StartCodeOffset] = 0x00; // DMX start code
        slots.CopyTo(destination[HeaderLength..]);
        return total;
    }

    private static void WriteFlagsAndLength(Span<byte> destination, int length) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(PduFlags | (length & 0x0FFF)));
}
