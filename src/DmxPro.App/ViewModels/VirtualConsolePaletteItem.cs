namespace DmxPro.App.ViewModels;

/// <summary>One entry of the edit-mode widget palette.</summary>
public sealed record VirtualConsolePaletteItem(VirtualConsoleWidgetKind Kind, string Name, string Icon, string Description);
