using DmxPro.Core.Validation;

namespace DmxPro.Output.Configuration;

/// <summary>Enttec DMX USB Pro (and compatible widgets) through its virtual COM port. One universe per device.</summary>
public sealed class EnttecProOutputConfig : OutputConfig
{
    /// <summary>Windows COM port name, e.g. <c>COM3</c>.</summary>
    public string PortName { get; set; } = string.Empty;

    public override OutputKind Kind => OutputKind.EnttecPro;

    public override string? DeviceKey =>
        string.IsNullOrWhiteSpace(PortName) ? null : "serial:" + PortName.Trim().ToUpperInvariant();

    protected override string DefaultName => "Enttec DMX USB Pro";

    protected override void ValidateCore(List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(PortName))
        {
            issues.Add(Error("SERIAL_PORT_MISSING", "choose the COM port of the interface."));
        }

        if (Universes is { Count: > 1 })
        {
            issues.Add(Error("USB_SINGLE_UNIVERSE", "a USB-DMX interface sends exactly one universe."));
        }
    }
}
