namespace DmxPro.App.Services;

/// <summary>Well-known folders used by the app.</summary>
public sealed class AppPaths
{
    public string BuiltInProfilesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "profiles");

    /// <summary>User and imported profiles: <c>%APPDATA%\DmxPro\profiles</c>.</summary>
    public string UserProfilesDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DmxPro", "profiles");

    public string DefaultProjectsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DmxPro");

    /// <summary>Cached music analyses (waveform, tempo): <c>%LOCALAPPDATA%\DmxPro\AudioCache</c>. Safe to delete.</summary>
    public string AudioCacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DmxPro", "AudioCache");
}
