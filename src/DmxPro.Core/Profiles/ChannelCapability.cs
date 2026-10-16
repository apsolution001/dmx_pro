namespace DmxPro.Core.Profiles;

/// <summary>A documented value range on a channel, e.g. "10–255 = strobe slow → fast".</summary>
public sealed class ChannelCapability
{
    public int Min { get; set; }

    public int Max { get; set; } = 255;

    public string Label { get; set; } = string.Empty;

    /// <summary>True when this range is a safe resting state (e.g. "no strobe", "manual color mode").</summary>
    public bool Safe { get; set; }

    public bool Contains(byte value) => value >= Min && value <= Max;

    public ChannelCapability Clone() => (ChannelCapability)MemberwiseClone();
}
