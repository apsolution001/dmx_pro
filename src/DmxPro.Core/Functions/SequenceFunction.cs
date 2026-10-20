using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// A chaser whose steps hold their own channel values for a fixed set of fixtures (instead of referencing other
/// functions). Timing, run order and direction work like <see cref="ChaserFunction"/>.
/// </summary>
public sealed class SequenceFunction : LightFunction
{
    [JsonIgnore]
    public override FunctionType Type => FunctionType.Sequence;

    /// <summary>The fixtures the steps are edited for. Playback uses the step values; this list is for the editor.</summary>
    public List<Guid> FixtureIds { get; set; } = [];

    public List<SequenceStep> Steps { get; set; } = [];

    public RunOrder RunOrder { get; set; } = RunOrder.Loop;

    public RunDirection Direction { get; set; } = RunDirection.Forward;

    protected override LightFunction CloneCore() => new SequenceFunction
    {
        FixtureIds = [.. FixtureIds],
        Steps = Steps.Select(s => s.Clone()).ToList(),
        RunOrder = RunOrder,
        Direction = Direction,
    };
}
