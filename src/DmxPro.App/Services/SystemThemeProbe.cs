using Microsoft.Win32;

namespace DmxPro.App.Services;

/// <summary>Reads the Windows "default app mode" (dark / light) for the Follow Windows theme setting.</summary>
public static class SystemThemeProbe
{
    /// <summary>True when Windows apps use dark mode; dark when the value cannot be read (the app default).</summary>
    public static bool IsWindowsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}
