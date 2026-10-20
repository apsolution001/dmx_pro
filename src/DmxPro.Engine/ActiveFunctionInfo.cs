namespace DmxPro.Engine;

/// <summary>A function instance that was running when a snapshot was taken.</summary>
/// <param name="FunctionId">Id of the running function.</param>
/// <param name="Layer">Layer it plays in.</param>
/// <param name="Level">Current fade level 0..1 (before the function intensity).</param>
/// <param name="Intensity">Function intensity 0..1.</param>
/// <param name="IsStopping">True while it fades out after a stop.</param>
/// <param name="Speed">Speed factor of the function's own time (1 = as programmed).</param>
/// <param name="StrobeLimit">Why a strobe in this function is not flashing as programmed (safety rule 8).</param>
public readonly record struct ActiveFunctionInfo(
    Guid FunctionId,
    EngineLayer Layer,
    double Level,
    double Intensity,
    bool IsStopping,
    double Speed = 1,
    StrobeLimit StrobeLimit = StrobeLimit.None);
