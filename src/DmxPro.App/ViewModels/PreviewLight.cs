namespace DmxPro.App.ViewModels;

/// <summary>
/// One circle of the on-screen stage preview: a fixture and its approximate light colour as "#RRGGBB"
/// (a real light colour, so a hex value is allowed; WPF converts it to a brush).
/// </summary>
public sealed record PreviewLight(string Name, string Color)
{
    public string ToolTip => $"{Name} — {Color}";

    public static string ToHex((byte R, byte G, byte B) color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
