namespace DmxPro.App.ViewModels;

/// <summary>One channel of the selected mode in the Add Fixture dialog (number within the fixture, name, type).</summary>
public sealed record ModeChannelRow(int Number, string Name, string Type, bool IsGeneric);
