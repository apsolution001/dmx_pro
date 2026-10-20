using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// Plays other functions (usually scenes) one after another. Each step has its own fade in / hold / fade out; the
/// chaser loops, runs once or ping-pongs, forward or backward. Steps reference functions by id, so editing a scene
/// changes every chaser that uses it.
/// </summary>
public sealed class ChaserFunction : LightFunction
{
    [JsonIgnore]
    public override FunctionType Type => FunctionType.Chaser;

    public List<ChaserStep> Steps { get; set; } = [];

    public RunOrder RunOrder { get; set; } = RunOrder.Loop;

    public RunDirection Direction { get; set; } = RunDirection.Forward;

    /// <summary>Appends a step that plays <paramref name="functionId"/> and returns it.</summary>
    public ChaserStep AddStep(Guid functionId, double holdSeconds = ChaserStep.DefaultHoldSeconds, double fadeInSeconds = 0, double fadeOutSeconds = 0)
    {
        var step = new ChaserStep
        {
            FunctionId = functionId,
            HoldSeconds = holdSeconds,
            FadeInSeconds = fadeInSeconds,
            FadeOutSeconds = fadeOutSeconds,
        };
        Steps.Add(step);
        return step;
    }

    protected override LightFunction CloneCore() => new ChaserFunction
    {
        Steps = Steps.Select(s => s.Clone()).ToList(),
        RunOrder = RunOrder,
        Direction = Direction,
    };
}
