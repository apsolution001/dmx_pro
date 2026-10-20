using DmxPro.Core.Profiles;

namespace DmxPro.App.ViewModels;

/// <summary>
/// One entry of the Channels tab's "Set on selected lights" picker: a channel type the selected lights have
/// (resolved from their profiles) and how many of their channels it covers.
/// </summary>
public sealed record SceneChannelTypeOption(ChannelType Type, int ChannelCount, int LightCount)
{
    public string Label => $"{TypeName(Type)} · {LightCount} {(LightCount == 1 ? "light" : "lights")}, {ChannelCount} ch";

    /// <summary>Whether a channel type can be set on many lights at once (its meaning is known and not relative).</summary>
    public static bool IsBulkSettable(ChannelType type) => type is not (ChannelType.Generic or ChannelType.Fine);

    public static string TypeName(ChannelType type) => type switch
    {
        ChannelType.ColorMacro => "Colour macro",
        ChannelType.UV => "UV",
        _ => type.ToString(),
    };
}
