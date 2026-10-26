namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// The on-screen colour of one light in a sequence step ("#RRGGBB", a real light colour for display only) and the
/// tooltip that names it, so the swatch never carries meaning by colour alone.
/// </summary>
public sealed record SequenceSwatch(string Hex, string ToolTip);
