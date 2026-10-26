namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One colour slot of the colour cycle preview strip: the colour holds for <see cref="HoldShare"/> of the slot, then
/// blends into <see cref="NextHex"/>. Hex values are real light colours (on-screen only).
/// </summary>
public sealed record ColorCyclePreviewSlot(string Hex, string NextHex, double HoldShare, string ToolTip);
