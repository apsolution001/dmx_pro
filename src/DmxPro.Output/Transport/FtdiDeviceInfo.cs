namespace DmxPro.Output.Transport;

/// <summary>One FTDI device as reported by the D2XX driver.</summary>
/// <param name="Index">Index for <c>OpenDmxOutputConfig.DeviceIndex</c>.</param>
/// <param name="SerialNumber">Stable identifier; prefer it over the index.</param>
/// <param name="Description">Product string, e.g. "FT232R USB UART" or "DMX USB PRO".</param>
/// <param name="IsOpen">Already opened by a program.</param>
public sealed record FtdiDeviceInfo(int Index, string SerialNumber, string Description, bool IsOpen);
