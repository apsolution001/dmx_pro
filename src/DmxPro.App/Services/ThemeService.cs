using Wpf.Ui.Appearance;

namespace DmxPro.App.Services;

public sealed class ThemeService : IThemeService
{
    // App.xaml starts in dark (operators work in dark venues).
    public bool IsDark { get; private set; } = true;

    public void SetDark(bool dark)
    {
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        IsDark = dark;
    }
}
