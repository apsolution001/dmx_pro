namespace DmxPro.Core.VirtualConsole;

/// <summary>
/// Starts/stops one function. Drawn as a button (<see cref="Mode"/> decides toggle or flash) or, when the page's
/// <see cref="ConsolePage.ShowButtonsAsSliders"/> is on, as an intensity slider for the same function, label and colour.
/// </summary>
public sealed class FunctionWidget : ConsoleWidget
{
    /// <summary>The function this widget controls; <see cref="Guid.Empty"/> while not assigned yet.</summary>
    public Guid FunctionId { get; set; }

    /// <summary>Button behaviour. Ignored while the widget is shown as a slider, and kept for switching back.</summary>
    public FunctionWidgetMode Mode { get; set; } = FunctionWidgetMode.Toggle;
}
