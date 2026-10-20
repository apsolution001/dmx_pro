using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// Moves from one scene's values to another's over <see cref="DurationSeconds"/>, then holds the target until stopped.
/// Channels in both scenes interpolate linearly; channels only in the target fade in from whatever is underneath; channels
/// only in the source fade out to whatever is underneath. Without a source the target simply fades in from underneath.
/// </summary>
public sealed class FadeFunction : LightFunction
{
    public const double DefaultDurationSeconds = 3.0;

    [JsonIgnore]
    public override FunctionType Type => FunctionType.Fade;

    /// <summary>Scene to start from; null = from whatever is underneath (profile defaults when nothing runs).</summary>
    public Guid? FromSceneId { get; set; }

    /// <summary>Scene to end at; null (not chosen yet) = the fade outputs nothing.</summary>
    public Guid? ToSceneId { get; set; }

    public double DurationSeconds { get; set; } = DefaultDurationSeconds;

    protected override LightFunction CloneCore() => new FadeFunction
    {
        FromSceneId = FromSceneId,
        ToSceneId = ToSceneId,
        DurationSeconds = DurationSeconds,
    };
}
