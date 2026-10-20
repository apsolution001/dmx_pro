using System.Windows.Input;

namespace DmxPro.App.Controls;

/// <summary>
/// Maps a pressed key to the name stored in a console widget's key shortcut ("A", "1", "F5"). Digits on the main row
/// and the number pad give the same name, so "1" works on both. F12 is reserved for the operator-mode toggle.
/// </summary>
public static class ConsoleKeyNames
{
    public static string? From(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }

        if (key is (>= Key.A and <= Key.Z) or (>= Key.F1 and <= Key.F11))
        {
            return key.ToString().ToUpperInvariant();
        }

        // Navigation and editing keys stay with the UI (focus, buttons, text boxes).
        return null;
    }
}
