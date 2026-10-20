namespace DmxPro.Core.Functions;

/// <summary>
/// One step of a <see cref="SequenceFunction"/>: its own channel values (like a small scene) with fade in / hold /
/// fade out timing that works exactly like <see cref="ChaserStep"/>.
/// </summary>
public sealed class SequenceStep
{
    /// <summary>Optional label shown in the editor ("Step 1" when empty).</summary>
    public string? Name { get; set; }

    public double FadeInSeconds { get; set; }

    public double HoldSeconds { get; set; } = ChaserStep.DefaultHoldSeconds;

    public double FadeOutSeconds { get; set; }

    /// <summary>Channel values of this step, addressed by fixture + profile channel key (never offsets).</summary>
    public List<SceneValue> Values { get; set; } = [];

    /// <summary>Sets (or adds) the value of one fixture channel.</summary>
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

    /// <summary>The stored value, or null when the step does not touch that channel.</summary>
    public byte? GetValue(Guid fixtureId, string channelKey) => Find(fixtureId, channelKey)?.Value;

    public SequenceStep Clone() => new()
    {
        Name = Name,
        FadeInSeconds = FadeInSeconds,
        HoldSeconds = HoldSeconds,
        FadeOutSeconds = FadeOutSeconds,
        Values = Values.Select(v => v.Clone()).ToList(),
    };

    private SceneValue? Find(Guid fixtureId, string channelKey) =>
        Values.FirstOrDefault(v => v.FixtureId == fixtureId && string.Equals(v.ChannelKey, channelKey, StringComparison.Ordinal));
}
