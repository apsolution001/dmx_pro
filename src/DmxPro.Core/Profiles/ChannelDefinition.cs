namespace DmxPro.Core.Profiles;

/// <summary>One channel a fixture can expose. Modes pick channels by <see cref="Key"/>.</summary>
public sealed class ChannelDefinition
{
    /// <summary>Unique within the profile; referenced by <see cref="FixtureMode.Channels"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ChannelType Type { get; set; } = ChannelType.Generic;

    /// <summary>Value used at start and on release.</summary>
    public byte Default { get; set; }

    /// <summary>For <see cref="ChannelType.Fine"/> channels: key of the coarse (high byte) channel.</summary>
    public string? CoarseKey { get; set; }

    public string? Description { get; set; }

    public List<ChannelCapability> Capabilities { get; set; } = [];

    public ChannelCapability? FindCapability(byte value) => Capabilities.FirstOrDefault(c => c.Contains(value));

    public ChannelDefinition Clone()
    {
        var copy = (ChannelDefinition)MemberwiseClone();
        copy.Capabilities = Capabilities.Select(c => c.Clone()).ToList();
        return copy;
    }
}
