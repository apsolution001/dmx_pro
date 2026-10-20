namespace DmxPro.App.ViewModels;

/// <summary>A function, show or group a widget can control (<see cref="Guid.Empty"/> = none / all).</summary>
public sealed record VirtualConsoleTargetOption(Guid Id, string Name);
