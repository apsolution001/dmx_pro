namespace DmxPro.Engine.Runtime;

internal enum EngineCommandKind
{
    StartFunction,
    StopFunction,
    SetIntensity,

    /// <summary>Change the speed factor of a running function (<see cref="EngineCommand.Value"/>).</summary>
    SetSpeed,
    StopPreview,
    SetOverride,
    ClearOverride,
    ClearOverrides,
    ReleaseAll,
    StartShow,
    StopShow,

    /// <summary>Freeze a playing show's position (it keeps rendering).</summary>
    PauseShow,

    /// <summary>Continue a paused show from its frozen position.</summary>
    ResumeShow,

    /// <summary>Move a playing or paused show to <see cref="EngineCommand.Seconds"/> (clamped to 0..length).</summary>
    SeekShow,

    /// <summary>Swap in a new model; running functions/shows are re-bound by id.</summary>
    RefreshModel,

    /// <summary>Release everything, then swap in a new model (project open).</summary>
    LoadModel,
}
