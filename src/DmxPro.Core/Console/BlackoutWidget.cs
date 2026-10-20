namespace DmxPro.Core.VirtualConsole;

/// <summary>
/// Blackout toggle: sends a dark state while on. It does NOT disable output (safety rule 3); there is deliberately no
/// console widget that enables or disables output.
/// </summary>
public sealed class BlackoutWidget : ConsoleWidget;
