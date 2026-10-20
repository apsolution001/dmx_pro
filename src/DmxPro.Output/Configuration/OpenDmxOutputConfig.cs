using DmxPro.Core.Validation;

namespace DmxPro.Output.Configuration;

/// <summary>
/// Enttec Open DMX USB and other "dumb" FTDI dongles, driven through the FTDI D2XX driver. The PC generates the DMX
/// timing, so output may jitter under load (best effort). One universe per device.
/// </summary>
public sealed class OpenDmxOutputConfig : OutputConfig
{
    /// <summary>A full 513-slot frame plus break takes ~23 ms at 250 kbaud; 30 Hz leaves headroom on a busy PC.</summary>
    public const int DefaultOpenDmxFrameRateHz = 30;

    public OpenDmxOutputConfig()
    {
        FrameRateHz = DefaultOpenDmxFrameRateHz;
    }

    /// <summary>FTDI serial number to open; empty = use <see cref="DeviceIndex"/>.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Index in the FTDI device list, used when no serial number is set.</summary>
    public int DeviceIndex { get; set; }

    public override OutputKind Kind => OutputKind.OpenDmx;

    public override string? DeviceKey =>
        string.IsNullOrWhiteSpace(SerialNumber) ? "ftdi-index:" + DeviceIndex : "ftdi:" + SerialNumber.Trim();

    protected override string DefaultName => "Open DMX USB";

    protected override void ValidateCore(List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(SerialNumber) && DeviceIndex < 0)
        {
            issues.Add(Error("FTDI_DEVICE_INDEX_RANGE", $"device index {DeviceIndex} is invalid."));
        }

        if (Universes is { Count: > 1 })
        {
            issues.Add(Error("USB_SINGLE_UNIVERSE", "a USB-DMX interface sends exactly one universe."));
        }
    }
}
