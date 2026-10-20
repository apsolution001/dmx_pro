using DmxPro.Engine;

namespace DmxPro.App.Services;

/// <summary>
/// Pushes the app settings that affect playback (strobe limits, console override mode, safe frame on exit) into the
/// engine at start and whenever the settings change.
/// </summary>
public sealed class SettingsEngineBridge
{
    private readonly AppSettingsService _settings;
    private readonly DmxEngine _engine;
    private readonly EngineRuntimeService _runtime;

    public SettingsEngineBridge(AppSettingsService settings, DmxEngine engine, EngineRuntimeService runtime)
    {
        _settings = settings;
        _engine = engine;
        _runtime = runtime;
    }

    /// <summary>Applies the current settings and keeps applying later changes.</summary>
    public void Start()
    {
        Apply();
        _settings.Changed += (_, _) => Apply();
    }

    private void Apply()
    {
        var current = _settings.Current;
        _engine.AllowStrobe = current.AllowStrobe;
        _engine.MaxStrobeRateHz = current.MaxStrobeRateHz;
        _engine.ShowOverrideMode = current.ConsoleOverrideMode == ShowOverrideModeSetting.OnlyTouchedLights
            ? ShowOverrideMode.TouchedChannelsOnly
            : ShowOverrideMode.MuteWholeShow;
        _runtime.SafeFrameMode = current.SafeFrameOnExit == SafeFrameSetting.ProfileDefaults
            ? SafeFrameMode.ProfileDefaults
            : SafeFrameMode.Blackout;
    }
}
