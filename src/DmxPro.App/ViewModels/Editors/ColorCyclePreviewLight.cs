namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One light of the colour cycle preview: the colour it shows when the cycle starts, so the "Spread across lights"
/// setting can be seen on screen. <see cref="Hex"/> is a real light colour (on-screen only).
/// </summary>
public sealed record ColorCyclePreviewLight(int Number, string Hex, string ColorName, string LightName)
{
    /// <summary>"Light 2, PAR 2: Orange".</summary>
    public string Description => $"Light {Number}, {LightName}: {ColorName}";
}
