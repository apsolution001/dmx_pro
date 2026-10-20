namespace DmxPro.Core.VirtualConsole;

/// <summary>Starts/stops a show (timeline) from the console. Always a button.</summary>
public sealed class ShowWidget : ConsoleWidget
{
    /// <summary>The show this widget plays; <see cref="Guid.Empty"/> while not assigned yet.</summary>
    public Guid ShowId { get; set; }
}
