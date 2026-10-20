namespace DmxPro.App.Services;

/// <summary>A network adapter to send Art-Net / sACN from.</summary>
/// <param name="Address">IPv4 address to bind to; empty = let Windows choose.</param>
/// <param name="Label">Shown in the picker, e.g. "Ethernet — 192.168.1.10".</param>
/// <param name="Broadcast">Directed broadcast address of the adapter's subnet (e.g. 192.168.1.255), or null.</param>
public sealed record NetworkAdapterOption(string Address, string Label, string? Broadcast);
