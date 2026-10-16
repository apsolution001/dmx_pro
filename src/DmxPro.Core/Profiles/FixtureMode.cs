namespace DmxPro.Core.Profiles;

/// <summary>A DMX mode: the ordered channel layout the fixture uses when set to this mode.</summary>
public sealed class FixtureMode
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Channel keys in DMX order; index 0 sits at the fixture's start address.</summary>
    public List<string> Channels { get; set; } = [];

    public int ChannelCount => Channels.Count;

    public FixtureMode Clone() => new() { Name = Name, Channels = [.. Channels] };
}
