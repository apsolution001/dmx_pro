using System.Windows;

namespace DmxPro.App.Services;

/// <summary>
/// Picks the window every dialog should open over: the active one, so a dialog triggered from the live Stage window
/// (maybe on a second monitor) is not hidden behind the main window.
/// </summary>
internal static class DialogOwner
{
    public static Window? ActiveOwner()
    {
        var app = Application.Current;
        return app?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app?.MainWindow;
    }
}
