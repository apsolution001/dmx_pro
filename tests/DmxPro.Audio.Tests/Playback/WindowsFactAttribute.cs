namespace DmxPro.Audio.Tests.Playback;

/// <summary>A fact that needs Windows Media Foundation; skipped on other systems.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs Windows Media Foundation.";
        }
    }
}
