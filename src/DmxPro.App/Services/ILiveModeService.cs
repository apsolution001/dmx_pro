namespace DmxPro.App.Services;

/// <summary>
/// "Go live" (owner decision 2026-10-22, like QLC+ Operate mode): while live, only the Virtual Console drives the
/// lights — console functions, console shows, Grand Master, Blackout and Release All. Function-editor previews, Test &amp;
/// Preview channel values and shows played from the Shows page are suppressed at engine level
/// (<see cref="DmxPro.Engine.DmxEngine.SetLiveMode"/>). A mode switch only: output enable/disable is separate and
/// unchanged (safety rules 1–3). Always off on app start and on every project open; never persisted.
/// </summary>
public interface ILiveModeService
{
    /// <summary>Raised on the UI thread after <see cref="IsLive"/> changed.</summary>
    event EventHandler? LiveModeChanged;

    /// <summary>True while live mode is on.</summary>
    bool IsLive { get; }

    /// <summary>
    /// Switches live mode. On: the preview and every test value are released and Shows-page shows stop, at the next
    /// engine tick. Off: what the console runs keeps running (the lights do not jump); the other sources are allowed again.
    /// </summary>
    void SetLive(bool live);
}
