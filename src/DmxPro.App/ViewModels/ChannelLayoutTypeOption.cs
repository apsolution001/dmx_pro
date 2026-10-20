using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One entry of the channel layout editor's Function list: the channel type with its label, icon, the short chip
/// text of the preview strip and (for colour channels) the light colour.
/// </summary>
public sealed record ChannelLayoutTypeOption(ChannelType Type, string Label, string IconKind, string ChipText, string? ColorHex)
{
    /// <summary>All selectable functions, in the order the editor lists them.</summary>
    public static IReadOnlyList<ChannelLayoutTypeOption> All { get; } =
    [
        new(ChannelType.Dimmer, "Dimmer", "Dimmer", "D", null),
        new(ChannelType.Red, "Red", "Color", "R", "#FF0000"),
        new(ChannelType.Green, "Green", "Color", "G", "#00FF00"),
        new(ChannelType.Blue, "Blue", "Color", "B", "#0000FF"),
        new(ChannelType.White, "White", "Color", "W", "#FFFFFF"),
        new(ChannelType.Amber, "Amber", "Color", "A", "#FFBF00"),
        new(ChannelType.UV, "UV", "Color", "UV", "#8A2BE2"),
        new(ChannelType.Strobe, "Strobe", "Strobe", "S", null),
        new(ChannelType.ColorMacro, "Color macro", "ColorCycle", "CM", null),
        new(ChannelType.Program, "Program", "Sequence", "P", null),
        new(ChannelType.Speed, "Speed", "Timeline", "Sp", null),
        new(ChannelType.Mode, "Mode", "Settings", "M", null),
        new(ChannelType.Fine, "Fine (16-bit)", "Channel", "F", null),
        new(ChannelType.Generic, "Unknown", "Warning", "?", null),
    ];

    public static ChannelLayoutTypeOption For(ChannelType type) =>
        All.FirstOrDefault(o => o.Type == type) ?? All[^1];
}
