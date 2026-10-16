namespace DmxPro.Core.Profiles;

public static class ChannelTypeExtensions
{
    /// <summary>
    /// Intensity channels merge HTP and are scaled by Grand Master / Blackout; everything else merges LTP.
    /// </summary>
    public static bool IsIntensity(this ChannelType type) => type is
        ChannelType.Dimmer or ChannelType.Red or ChannelType.Green or ChannelType.Blue or
        ChannelType.White or ChannelType.Amber or ChannelType.UV;

    public static bool IsColor(this ChannelType type) => type is
        ChannelType.Red or ChannelType.Green or ChannelType.Blue or
        ChannelType.White or ChannelType.Amber or ChannelType.UV;
}
