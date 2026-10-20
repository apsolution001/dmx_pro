namespace DmxPro.Core.Functions;

/// <summary>
/// One stored channel value of a scene. The channel is identified by its profile channel <see cref="ChannelKey"/>,
/// never by an offset or address, so it stays correct when the fixture is re-addressed.
/// </summary>
public sealed class SceneValue
{
    public Guid FixtureId { get; set; }

    public string ChannelKey { get; set; } = string.Empty;

    public byte Value { get; set; }

    public SceneValue Clone() => new() { FixtureId = FixtureId, ChannelKey = ChannelKey, Value = Value };
}
