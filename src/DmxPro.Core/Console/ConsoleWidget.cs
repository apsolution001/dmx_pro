using System.Text.Json.Serialization;

namespace DmxPro.Core.VirtualConsole;

/// <summary>
/// Base of every Virtual Console widget. Position and size are in whole grid cells of the page
/// (<see cref="ConsolePage.Columns"/> × <see cref="ConsolePage.Rows"/>), origin top-left. Saved polymorphically with a
/// <c>type</c> discriminator. Runtime state (pressed, running, fader level) is never part of the layout.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FunctionWidget), "Function")]
[JsonDerivedType(typeof(ShowWidget), "Show")]
[JsonDerivedType(typeof(GroupLevelWidget), "GroupLevel")]
[JsonDerivedType(typeof(MasterWidget), "Master")]
[JsonDerivedType(typeof(BlackoutWidget), "Blackout")]
[JsonDerivedType(typeof(ReleaseAllWidget), "ReleaseAll")]
[JsonDerivedType(typeof(SpeedWidget), "Speed")]
[JsonDerivedType(typeof(LabelWidget), "Label")]
public abstract class ConsoleWidget
{
    public const int DefaultWidth = 2;
    public const int DefaultHeight = 2;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Label { get; set; } = string.Empty;

    /// <summary>Display colour as <c>#RRGGBB</c>, or null for the theme default.</summary>
    public string? Color { get; set; }

    /// <summary>Left column (0-based grid cell).</summary>
    public int X { get; set; }

    /// <summary>Top row (0-based grid cell).</summary>
    public int Y { get; set; }

    /// <summary>Width in grid cells (at least 1).</summary>
    public int Width { get; set; } = DefaultWidth;

    /// <summary>Height in grid cells (at least 1).</summary>
    public int Height { get; set; } = DefaultHeight;

    /// <summary>Keyboard shortcut in display form, e.g. <c>Ctrl+1</c>; null for none.</summary>
    public string? KeyShortcut { get; set; }

    /// <summary>Copy with a new <see cref="Id"/>; every other property (including the target reference) is kept.</summary>
    public ConsoleWidget CloneAsNew()
    {
        // All widget properties are values, strings or Guids, so a member-wise copy is a full copy.
        var copy = (ConsoleWidget)MemberwiseClone();
        copy.Id = Guid.NewGuid();
        return copy;
    }

    /// <summary>Whether this widget overlaps the given rectangle (grid cells).</summary>
    public bool Overlaps(int x, int y, int width, int height) =>
        X < x + width && x < X + Width && Y < y + height && y < Y + Height;
}
