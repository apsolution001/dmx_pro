namespace DmxPro.Core.Profiles;

/// <summary>
/// What a DMX channel controls. The UI only shows controls for types a profile declares.
/// </summary>
public enum ChannelType
{
    /// <summary>Unknown or custom meaning. Shown only as a raw 0–255 control.</summary>
    Generic,
    Dimmer,
    Red,
    Green,
    Blue,
    White,
    Amber,
    UV,
    Strobe,
    ColorMacro,
    Program,
    Speed,
    Mode,
    /// <summary>Low byte of a 16-bit pair; <see cref="ChannelDefinition.CoarseKey"/> names the high byte.</summary>
    Fine,
}
