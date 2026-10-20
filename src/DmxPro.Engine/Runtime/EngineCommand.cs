namespace DmxPro.Engine.Runtime;

/// <summary>A command from any thread, applied by the engine thread at the start of the next tick.</summary>
/// <remarks><see cref="Seconds"/> carries show positions in full precision (a float loses milliseconds on long shows).</remarks>
internal readonly record struct EngineCommand(
    EngineCommandKind Kind,
    Guid Id = default,
    EngineLayer Layer = EngineLayer.Show,
    float Value = 0f,
    int Universe = 0,
    int Address = 0,
    EngineModel? Model = null,
    double Seconds = 0);
