namespace DmxPro.Core.VirtualConsole;

/// <summary>Speed control (with tap tempo) for functions that support speed.</summary>
public sealed class SpeedWidget : ConsoleWidget
{
    /// <summary>The function whose speed this controls, or null for every running function that supports speed.</summary>
    public Guid? FunctionId { get; set; }
}
