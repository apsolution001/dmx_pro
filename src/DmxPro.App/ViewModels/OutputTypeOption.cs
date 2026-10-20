namespace DmxPro.App.ViewModels;

/// <summary>An entry of the output type picker.</summary>
/// <param name="Type">The output type.</param>
/// <param name="Name">Short name shown in the picker.</param>
/// <param name="Description">One-line explanation (tooltip / hint).</param>
public sealed record OutputTypeOption(UniverseOutputType Type, string Name, string Description)
{
    public static IReadOnlyList<OutputTypeOption> All { get; } =
    [
        new(UniverseOutputType.None, "— None —", "This universe is not sent anywhere."),
        new(UniverseOutputType.Virtual, "Virtual (no hardware)", "Loopback for testing: nothing leaves this PC."),
        new(UniverseOutputType.ArtNet, "Art-Net", "DMX over the network to Art-Net nodes (UDP 6454)."),
        new(UniverseOutputType.Sacn, "sACN (E1.31)", "Streaming ACN over the network (UDP 5568), multicast or unicast."),
        new(UniverseOutputType.EnttecPro, "Enttec DMX USB Pro", "Enttec DMX USB Pro or compatible widget on a COM port."),
        new(UniverseOutputType.OpenDmx, "Open DMX USB (FTDI)", "Enttec Open DMX and other FTDI dongles (FTDI D2XX driver needed)."),
    ];
}
