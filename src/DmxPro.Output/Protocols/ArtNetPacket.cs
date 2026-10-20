using System.Buffers.Binary;

namespace DmxPro.Output.Protocols;

/// <summary>
/// Art-Net 4 ArtDmx (OpCode 0x5000) packet writer. Pure and allocation-free: writes into a caller-provided buffer.
/// </summary>
/// <remarks>
/// Layout: <c>"Art-Net\0"</c> · OpCode (LE) · ProtVer 14 (BE) · Sequence · Physical · SubUni · Net · Length (BE) · Data.
/// </remarks>
public static class ArtNetPacket
{
    public const int DefaultPort = 6454;
    public const ushort OpDmx = 0x5000;
    public const ushort ProtocolVersion = 14;
    public const int HeaderLength = 18;
    public const int MaxDataLength = 512;
    public const int MinDataLength = 2;
    public const int MaxPacketLength = HeaderLength + MaxDataLength;

    /// <summary>15-bit Port-Address.</summary>
    public const int MaxPortAddress = 0x7FFF;

    /// <summary>Net is the top 7 bits of the Port-Address.</summary>
    public const int MaxNet = 127;

    private static ReadOnlySpan<byte> Id => "Art-Net\0"u8;

    /// <summary>Length field for <paramref name="frameLength"/> slots: clamped to 512, at least 2, rounded up to even.</summary>
    public static int GetDataLength(int frameLength)
    {
        var length = Math.Clamp(frameLength, 0, MaxDataLength);
        if (length < MinDataLength)
        {
            return MinDataLength;
        }

        return (length & 1) == 0 ? length : length + 1;
    }

    /// <summary>Total packet size for <paramref name="frameLength"/> slots.</summary>
    public static int GetPacketLength(int frameLength) => HeaderLength + GetDataLength(frameLength);

    /// <summary>Next sequence number: 1–255 rolling (0 means "sequencing disabled" and is skipped).</summary>
    public static byte NextSequence(byte current) => current >= 255 ? (byte)1 : (byte)(current + 1);

    /// <summary>
    /// Writes an ArtDmx packet. Values beyond 512 are ignored; short or odd-length data is zero-padded.
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteArtDmx(
        Span<byte> destination,
        byte sequence,
        byte physical,
        ushort portAddress,
        ReadOnlySpan<byte> data)
    {
        if (portAddress > MaxPortAddress)
        {
            throw new ArgumentOutOfRangeException(nameof(portAddress), portAddress, "Port-Address is 15 bits (0–32767).");
        }

        var source = data.Length > MaxDataLength ? data[..MaxDataLength] : data;
        var length = GetDataLength(source.Length);
        var total = HeaderLength + length;
        if (destination.Length < total)
        {
            throw new ArgumentException($"Destination needs {total} bytes.", nameof(destination));
        }

        Id.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], OpDmx);
        BinaryPrimitives.WriteUInt16BigEndian(destination[10..], ProtocolVersion);
        destination[12] = sequence;
        destination[13] = physical;
        destination[14] = (byte)(portAddress & 0xFF); // SubUni: Sub-Net (high nibble) | Universe (low nibble)
        destination[15] = (byte)(portAddress >> 8);   // Net: 7 bits
        BinaryPrimitives.WriteUInt16BigEndian(destination[16..], (ushort)length);
        source.CopyTo(destination[HeaderLength..]);
        destination.Slice(HeaderLength + source.Length, length - source.Length).Clear();
        return total;
    }
}
