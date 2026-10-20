namespace DmxPro.Output.Protocols;

/// <summary>E1.31 framing-layer option bits.</summary>
[Flags]
public enum SacnOptions : byte
{
    None = 0,

    /// <summary>Bit 5: receivers should wait for a synchronization packet.</summary>
    ForceSynchronization = 0x20,

    /// <summary>Bit 6: this source stops sending this universe; receivers release it immediately.</summary>
    StreamTerminated = 0x40,

    /// <summary>Bit 7: data is for visualizers only, not for live output.</summary>
    PreviewData = 0x80,
}
