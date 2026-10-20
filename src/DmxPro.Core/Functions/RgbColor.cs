using System.Globalization;
using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>An 8-bit RGB colour, e.g. the preview colour of a scene or a colour of an effect. Saved as "#RRGGBB".</summary>
[JsonConverter(typeof(RgbColorJsonConverter))]
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>"#RRGGBB".</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>True when dark text reads better on this colour than light text (perceived luminance ≥ 50 %).</summary>
    [JsonIgnore]
    public bool IsLight => ((0.299 * R) + (0.587 * G) + (0.114 * B)) / 255.0 >= 0.5;

    /// <summary>Parses "#RRGGBB" or "RRGGBB" (hex digits in any case).</summary>
    public static bool TryParseHex(string? text, out RgbColor color)
    {
        color = default;
        var span = text.AsSpan().Trim();
        if (span.StartsWith("#"))
        {
            span = span[1..];
        }

        if (span.Length != 6
            || !byte.TryParse(span[..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(span[2..4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(span[4..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new RgbColor(r, g, b);
        return true;
    }
}
