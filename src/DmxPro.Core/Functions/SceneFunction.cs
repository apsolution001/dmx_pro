using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>A static look: fixed values for selected fixture channels, with fade in/out times.</summary>
public sealed class SceneFunction : LightFunction
{
    [JsonIgnore]
    public override FunctionType Type => FunctionType.Scene;

    public List<SceneValue> Values { get; set; } = [];

    public double FadeInSeconds { get; set; }

    public double FadeOutSeconds { get; set; }

    /// <summary>Sets (or adds) the value of one fixture channel, addressed by profile channel key.</summary>
    public void SetValue(Guid fixtureId, string channelKey, byte value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelKey);
        var existing = Find(fixtureId, channelKey);
        if (existing is null)
        {
            Values.Add(new SceneValue { FixtureId = fixtureId, ChannelKey = channelKey, Value = value });
        }
        else
        {
            existing.Value = value;
        }
    }

    /// <summary>The stored value, or null when the scene does not touch that channel.</summary>
    public byte? GetValue(Guid fixtureId, string channelKey) => Find(fixtureId, channelKey)?.Value;

    public bool RemoveValue(Guid fixtureId, string channelKey) =>
        Values.RemoveAll(v => v.FixtureId == fixtureId && string.Equals(v.ChannelKey, channelKey, StringComparison.Ordinal)) > 0;

    protected override LightFunction CloneCore() => new SceneFunction
    {
        Values = Values.Select(v => v.Clone()).ToList(),
        FadeInSeconds = FadeInSeconds,
        FadeOutSeconds = FadeOutSeconds,
    };

    private SceneValue? Find(Guid fixtureId, string channelKey) =>
        Values.FirstOrDefault(v => v.FixtureId == fixtureId && string.Equals(v.ChannelKey, channelKey, StringComparison.Ordinal));
}
