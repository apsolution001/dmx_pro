namespace DmxPro.App.Services;

/// <summary>Which Fluent theme the app uses (Settings ▸ Appearance).</summary>
public enum ThemeModeSetting
{
    /// <summary>Dark theme (default: operators work in dark venues).</summary>
    Dark,

    Light,

    /// <summary>Follow the Windows "app mode" setting, also when it changes while the app runs.</summary>
    FollowWindows,
}
