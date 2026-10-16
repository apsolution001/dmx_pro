namespace DmxPro.App.ViewModels;

/// <summary>One channel of the selected fixture: its DMX address and meaning from the profile.</summary>
public sealed record ChannelMapRow(int Number, int Address, string Name, string Type, bool IsGeneric);
