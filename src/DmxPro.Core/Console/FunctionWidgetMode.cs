namespace DmxPro.Core.VirtualConsole;

/// <summary>How a function widget behaves while shown as a button.</summary>
public enum FunctionWidgetMode
{
    /// <summary>Press starts the function, press again stops it.</summary>
    Toggle,

    /// <summary>The function runs only while the button is held (safety rule 5: released on button-up, focus loss, close).</summary>
    Flash,
}
