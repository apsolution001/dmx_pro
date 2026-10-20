namespace DmxPro.Core.VirtualConsole;

/// <summary>
/// One named page of the Virtual Console: a grid of <see cref="Columns"/> × <see cref="Rows"/> cells holding widgets.
/// Widgets may overlap; <see cref="FindFreeSlot"/> finds an empty spot for new or duplicated widgets.
/// </summary>
public sealed class ConsolePage
{
    public const int DefaultColumns = 24;
    public const int DefaultRows = 16;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int Columns { get; set; } = DefaultColumns;

    public int Rows { get; set; } = DefaultRows;

    /// <summary>
    /// Toolbar "buttons ↔ sliders" switch: when on, every <see cref="FunctionWidget"/> on this page is drawn as an
    /// intensity slider in the same cells, with the same function, label and colour. Nothing else changes, so switching
    /// back restores the buttons exactly.
    /// </summary>
    public bool ShowButtonsAsSliders { get; set; }

    public List<ConsoleWidget> Widgets { get; set; } = [];

    public ConsoleWidget? FindWidget(Guid id) => Widgets.FirstOrDefault(w => w.Id == id);

    /// <summary>How the UI should draw <paramref name="widget"/> on this page right now.</summary>
    public WidgetPresentation PresentationOf(ConsoleWidget widget) => widget switch
    {
        FunctionWidget => ShowButtonsAsSliders ? WidgetPresentation.Slider : WidgetPresentation.Button,
        MasterWidget or GroupLevelWidget or SpeedWidget => WidgetPresentation.Slider,
        LabelWidget => WidgetPresentation.Label,
        _ => WidgetPresentation.Button,
    };

    /// <summary>Whether the widget lies completely inside the page grid.</summary>
    public bool Contains(ConsoleWidget widget) =>
        widget.X >= 0 && widget.Y >= 0 && widget.Width >= 1 && widget.Height >= 1
        && widget.X + widget.Width <= Columns && widget.Y + widget.Height <= Rows;

    /// <summary>
    /// Top-left cell of the first empty area of <paramref name="width"/> × <paramref name="height"/> cells (row by row,
    /// left to right), or null when nothing fits. Widgets listed in <paramref name="ignore"/> do not count as occupied.
    /// </summary>
    public (int X, int Y)? FindFreeSlot(int width, int height, ConsoleWidget? ignore = null)
    {
        for (var y = 0; y + height <= Rows; y++)
        {
            for (var x = 0; x + width <= Columns; x++)
            {
                if (!Widgets.Any(w => !ReferenceEquals(w, ignore) && w.Overlaps(x, y, width, height)))
                {
                    return (x, y);
                }
            }
        }

        return null;
    }

    /// <summary>Snaps a size to whole cells and clamps it so the widget fits the page from its current position.</summary>
    internal void SetSize(ConsoleWidget widget, double width, double height)
    {
        widget.Width = Math.Clamp(Snap(width), 1, Math.Max(1, Columns - widget.X));
        widget.Height = Math.Clamp(Snap(height), 1, Math.Max(1, Rows - widget.Y));
    }

    /// <summary>Snaps a position to whole cells and clamps it so the whole widget stays on the page.</summary>
    internal void SetPosition(ConsoleWidget widget, double x, double y)
    {
        widget.X = Math.Clamp(Snap(x), 0, Math.Max(0, Columns - widget.Width));
        widget.Y = Math.Clamp(Snap(y), 0, Math.Max(0, Rows - widget.Height));
    }

    /// <summary>Clamps size to the page and then position, so a widget of any (loaded or new) geometry fits.</summary>
    internal void FitInside(ConsoleWidget widget)
    {
        widget.Width = Math.Clamp(widget.Width, 1, Math.Max(1, Columns));
        widget.Height = Math.Clamp(widget.Height, 1, Math.Max(1, Rows));
        SetPosition(widget, widget.X, widget.Y);
    }

    private static int Snap(double cells)
    {
        if (!double.IsFinite(cells))
        {
            throw new ArgumentOutOfRangeException(nameof(cells), cells, "Grid coordinates must be finite numbers.");
        }

        return (int)Math.Clamp(Math.Round(cells, MidpointRounding.AwayFromZero), int.MinValue / 2, int.MaxValue / 2);
    }
}
