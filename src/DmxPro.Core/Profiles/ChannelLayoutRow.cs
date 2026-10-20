namespace DmxPro.Core.Profiles;

/// <summary>
/// One channel in the channel layout editor, in DMX order (row 0 sits at the fixture's start address).
/// See <see cref="ChannelLayout"/>.
/// </summary>
public sealed class ChannelLayoutRow
{
    private ChannelType _type = ChannelType.Generic;

    /// <summary>
    /// What the channel does. Changing the type drops <see cref="Capabilities"/>: documented ranges belong to the
    /// meaning they were written for and must not be presented for a different one (safety rule 7).
    /// </summary>
    public ChannelType Type
    {
        get => _type;
        set
        {
            if (value != _type)
            {
                Capabilities = [];
            }

            _type = value;
        }
    }

    /// <summary>Display name; empty means <see cref="ChannelLayout.DefaultNameFor"/> is used.</summary>
    public string Name { get; set; } = "";

    /// <summary>Value used at start and on release.</summary>
    public byte DefaultValue { get; set; }

    /// <summary>
    /// Documented value ranges carried over from a template channel (e.g. "0–9 No strobe"). Not edited by the layout
    /// editor; cleared when <see cref="Type"/> changes. Set after <see cref="Type"/> in object initializers.
    /// </summary>
    public List<ChannelCapability> Capabilities { get; set; } = [];

    public ChannelLayoutRow Clone() => new()
    {
        Type = Type,
        Name = Name,
        DefaultValue = DefaultValue,
        Capabilities = Capabilities.Select(c => c.Clone()).ToList(),
    };
}
