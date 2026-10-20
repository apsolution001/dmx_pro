namespace DmxPro.App.ViewModels;

/// <summary>
/// A one-click colour (used by the colour editors): levels for Red/Green/Blue/White/Amber/UV. Each level only
/// reaches fixtures whose profile mode has that channel type.
/// </summary>
public sealed record SceneEditorColorPreset(string Name, string Swatch, byte Red, byte Green, byte Blue, byte White, byte Amber, byte Uv)
{
    public string ToolTip => $"Set the colour to {Name.ToLowerInvariant()}";

    public static IReadOnlyList<SceneEditorColorPreset> All { get; } =
    [
        new("Red", "#FF0000", 255, 0, 0, 0, 0, 0),
        new("Green", "#00FF00", 0, 255, 0, 0, 0, 0),
        new("Blue", "#0000FF", 0, 0, 255, 0, 0, 0),
        new("White", "#FFFFFF", 255, 255, 255, 255, 0, 0),
        new("Cyan", "#00FFFF", 0, 255, 255, 0, 0, 0),
        new("Magenta", "#FF00FF", 255, 0, 255, 0, 0, 0),
        new("Yellow", "#FFFF00", 255, 255, 0, 0, 0, 0),
        new("Amber", "#FFBF00", 255, 126, 0, 0, 255, 0),
        new("Warm white", "#FFD8A0", 255, 160, 60, 0, 120, 0),
    ];
}
