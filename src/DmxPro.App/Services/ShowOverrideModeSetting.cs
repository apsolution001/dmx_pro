namespace DmxPro.App.Services;

/// <summary>
/// What a running show does on the lights while a Virtual Console function is active (owner decision 2026-10-18).
/// App-level setting; the main session maps it to the engine's override mode.
/// </summary>
public enum ShowOverrideModeSetting
{
    /// <summary>Default: any active console function mutes the whole show on the lights; its timeline keeps running.</summary>
    MuteWholeShow,

    /// <summary>Only the channels the console functions touch are overridden; the rest of the show stays visible.</summary>
    OnlyTouchedLights,
}
