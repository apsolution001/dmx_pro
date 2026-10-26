using DmxPro.Core.Functions;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Plain names for colour cycle colours, so the list never relies on the swatch alone: the exact name of a preset or
/// quick-fill colour ("Warm white"), otherwise the nearest everyday name from the hue ("Orange", "Dark blue").
/// </summary>
public static class ColorCycleColorNames
{
    private static readonly Dictionary<RgbColor, string> Known = BuildKnown();

    /// <summary>"Red", "Warm white", "Dark purple", "Off (black)" …</summary>
    public static string NameOf(RgbColor color)
    {
        if (Known.TryGetValue(color, out var name))
        {
            return name;
        }

        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (max < 0.08)
        {
            return "Off (black)";
        }

        var saturation = delta / max;
        if (saturation < 0.2)
        {
            return max >= 0.75 ? "White" : max >= 0.35 ? "Grey" : "Dark grey";
        }

        double hue;
        if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        var hueName = HueName(hue);
        if (max < 0.45)
        {
            return "Dark " + hueName.ToLowerInvariant();
        }

        return saturation < 0.5 ? "Light " + hueName.ToLowerInvariant() : hueName;
    }

    private static string HueName(double hue) => hue switch
    {
        < 12 => "Red",
        < 40 => "Orange",
        < 68 => "Yellow",
        < 100 => "Yellow-green",
        < 150 => "Green",
        < 172 => "Turquoise",
        < 200 => "Cyan",
        < 225 => "Sky blue",
        < 255 => "Blue",
        < 285 => "Purple",
        < 320 => "Magenta",
        < 345 => "Pink",
        _ => "Red",
    };

    private static Dictionary<RgbColor, string> BuildKnown()
    {
        var known = new Dictionary<RgbColor, string>
        {
            [new RgbColor(0, 0, 0)] = "Off (black)",
        };

        foreach (var preset in SceneEditorColorPreset.All)
        {
            if (RgbColor.TryParseHex(preset.Swatch, out var color))
            {
                known.TryAdd(color, preset.Name);
            }
        }

        foreach (var (color, name) in ColorCyclePalette.NamedColors)
        {
            known.TryAdd(color, name);
        }

        return known;
    }
}
