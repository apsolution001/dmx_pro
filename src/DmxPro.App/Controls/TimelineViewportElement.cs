using System.Windows;

namespace DmxPro.App.Controls;

/// <summary>
/// Base of the Shows timeline's drawn layers (ruler, beat grid, waveform). They sit in the scrolled timeline content,
/// which is as wide as the whole show (hundreds of thousands of pixels for a long song at high zoom), but draw only a
/// window around the visible part: the viewport plus one viewport width on each side. Scrolling inside that window
/// costs nothing (the scroll viewer just moves the existing drawing); leaving it re-renders once. Bind
/// <see cref="ViewportStart"/> / <see cref="ViewportWidth"/> to the scroll viewer's <c>HorizontalOffset</c> /
/// <c>ViewportWidth</c>; without them the whole element is drawn.
/// </summary>
public abstract class TimelineViewportElement : FrameworkElement
{
    /// <summary>Identifies the <see cref="PixelsPerSecond"/> dependency property.</summary>
    public static readonly DependencyProperty PixelsPerSecondProperty = DependencyProperty.Register(
        nameof(PixelsPerSecond), typeof(double), typeof(TimelineViewportElement),
        new FrameworkPropertyMetadata(40.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="ViewportStart"/> dependency property.</summary>
    public static readonly DependencyProperty ViewportStartProperty = DependencyProperty.Register(
        nameof(ViewportStart), typeof(double), typeof(TimelineViewportElement),
        new FrameworkPropertyMetadata(0.0, OnViewportChanged));

    /// <summary>Identifies the <see cref="ViewportWidth"/> dependency property.</summary>
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(TimelineViewportElement),
        new FrameworkPropertyMetadata(0.0, OnViewportChanged));

    // Pixel range drawn by the last render; empty (to < from) until the first render.
    private double _drawnFrom;
    private double _drawnTo = -1;

    /// <summary>Initializes a new instance of the <see cref="TimelineViewportElement"/> class.</summary>
    protected TimelineViewportElement()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>Timeline zoom: pixels per second of show time.</summary>
    public double PixelsPerSecond
    {
        get => (double)GetValue(PixelsPerSecondProperty);
        set => SetValue(PixelsPerSecondProperty, value);
    }

    /// <summary>Left edge of the visible part, in this element's pixels (the scroll offset).</summary>
    public double ViewportStart
    {
        get => (double)GetValue(ViewportStartProperty);
        set => SetValue(ViewportStartProperty, value);
    }

    /// <summary>Width of the visible part in pixels; 0 = unknown (draw everything).</summary>
    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    /// <summary>Zoom when it is usable (positive and finite), else 0.</summary>
    protected double SafePixelsPerSecond => PixelsPerSecond is var pps && double.IsFinite(pps) && pps > 0 ? pps : 0;

    /// <summary>
    /// Call at the start of <see cref="UIElement.OnRender"/>: the pixel range to draw and remembers it, so scrolling
    /// within it does not re-render. False when there is nothing to draw (no size).
    /// </summary>
    protected bool TryBeginRender(out double from, out double to)
    {
        var width = ActualWidth;
        if (width <= 0 || ActualHeight <= 0)
        {
            _drawnFrom = 0;
            _drawnTo = -1;
            from = to = 0;
            return false;
        }

        var start = ViewportStart;
        var viewport = ViewportWidth;
        if (viewport > 0 && double.IsFinite(viewport) && double.IsFinite(start))
        {
            from = Math.Max(0, start - viewport);
            to = Math.Min(width, start + (2 * viewport));
        }
        else
        {
            from = 0;
            to = width;
        }

        _drawnFrom = from;
        _drawnTo = to;
        return to > from;
    }

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (TimelineViewportElement)d;
        var viewport = element.ViewportWidth;
        if (!(viewport > 0) || !double.IsFinite(viewport))
        {
            element.InvalidateVisual();
            return;
        }

        // Only re-render when the visible part leaves the drawn window (clipped to the element's own width).
        var visibleFrom = Math.Max(0, element.ViewportStart);
        var visibleTo = Math.Min(element.ActualWidth, element.ViewportStart + viewport);
        if (visibleFrom < element._drawnFrom - 0.5 || visibleTo > element._drawnTo + 0.5)
        {
            element.InvalidateVisual();
        }
    }
}
