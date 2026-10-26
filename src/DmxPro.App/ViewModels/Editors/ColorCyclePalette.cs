using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// A quick-fill colour list of the colour cycle editor ("Rainbow", "Warm", "Cool"): one click replaces the colours.
/// <see cref="SwatchHexes"/> are real light colours for the button's mini strip.
/// </summary>
public sealed record ColorCyclePalette(string Name, string ToolTip, IReadOnlyList<RgbColor> Colors)
{
    private static readonly RgbColor Red = new(255, 0, 0);
    private static readonly RgbColor DeepOrange = new(255, 64, 0);
    private static readonly RgbColor Orange = new(255, 128, 0);
    private static readonly RgbColor Amber = new(255, 191, 0);
    private static readonly RgbColor Yellow = new(255, 255, 0);
    private static readonly RgbColor Green = new(0, 255, 0);
    private static readonly RgbColor Cyan = new(0, 255, 255);
    private static readonly RgbColor SkyBlue = new(0, 128, 255);
    private static readonly RgbColor Blue = new(0, 0, 255);
    private static readonly RgbColor Purple = new(128, 0, 255);
    private static readonly RgbColor Magenta = new(255, 0, 255);
    private static readonly RgbColor WarmWhite = new(255, 216, 160);

    /// <summary>"#RRGGBB" of every colour, for the button's mini strip.</summary>
    public IReadOnlyList<string> SwatchHexes { get; } = Colors.Select(c => c.ToHex()).ToArray();

    /// <summary>"Fill with Rainbow".</summary>
    public string AutomationName => $"Fill with {Name}";

    /// <summary>The quick fills, in button order.</summary>
    public static IReadOnlyList<ColorCyclePalette> All { get; } =
    [
        new("Rainbow", "Replace the colours with a rainbow: red, orange, yellow, green, cyan, blue, magenta", [Red, Orange, Yellow, Green, Cyan, Blue, Magenta]),
        new("Warm", "Replace the colours with warm tones: red, deep orange, orange, amber, warm white", [Red, DeepOrange, Orange, Amber, WarmWhite]),
        new("Cool", "Replace the colours with cool tones: blue, sky blue, cyan, purple", [Blue, SkyBlue, Cyan, Purple]),
    ];

    /// <summary>Names of the quick-fill colours that are not scene presets (used by <see cref="ColorCycleColorNames"/>).</summary>
    internal static IEnumerable<(RgbColor Color, string Name)> NamedColors =>
    [
        (DeepOrange, "Deep orange"),
        (Orange, "Orange"),
        (SkyBlue, "Sky blue"),
        (Purple, "Purple"),
    ];
}
