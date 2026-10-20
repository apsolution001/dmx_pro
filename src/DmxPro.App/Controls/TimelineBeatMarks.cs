using System.Windows;
using System.Windows.Media;
using DmxPro.Core.Shows;

namespace DmxPro.App.Controls;

/// <summary>
/// Draws the Shows timeline's beat and bar marks in one render pass, straight from the show's tempo and beat offset
/// (<see cref="TimelineSnap.BeatLines"/>, allocation-free) and only around the visible part (see
/// <see cref="TimelineViewportElement"/>). An element per mark meant thousands of elements to lay out for a long show.
/// When beats get closer than <see cref="MinBeatSpacing"/> pixels only bars are drawn; when bars do too, nothing.
/// </summary>
/// <remarks>
/// A height of <see cref="double.NaN"/> means the full height of the element (lane grid lines); a number draws a short
/// mark at the given top offset (ruler ticks).
/// </remarks>
public sealed class TimelineBeatMarks : TimelineViewportElement
{
    /// <summary>Closest spacing (pixels) at which beat lines are still drawn.</summary>
    public const double MinBeatSpacing = 6;

    /// <summary>Identifies the <see cref="Bpm"/> dependency property.</summary>
    public static readonly DependencyProperty BpmProperty = RenderProperty(nameof(Bpm), Show.DefaultBpm);

    /// <summary>Identifies the <see cref="BeatOffsetSeconds"/> dependency property.</summary>
    public static readonly DependencyProperty BeatOffsetSecondsProperty = RenderProperty(nameof(BeatOffsetSeconds), 0.0);

    /// <summary>Identifies the <see cref="BeatBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BeatBrushProperty = RenderProperty<Brush?>(nameof(BeatBrush), null);

    /// <summary>Identifies the <see cref="BarBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BarBrushProperty = RenderProperty<Brush?>(nameof(BarBrush), null);

    /// <summary>Identifies the <see cref="BeatWidth"/> dependency property.</summary>
    public static readonly DependencyProperty BeatWidthProperty = RenderProperty(nameof(BeatWidth), 1.0);

    /// <summary>Identifies the <see cref="BarWidth"/> dependency property.</summary>
    public static readonly DependencyProperty BarWidthProperty = RenderProperty(nameof(BarWidth), 1.0);

    /// <summary>Identifies the <see cref="BeatHeight"/> dependency property.</summary>
    public static readonly DependencyProperty BeatHeightProperty = RenderProperty(nameof(BeatHeight), double.NaN);

    /// <summary>Identifies the <see cref="BarHeight"/> dependency property.</summary>
    public static readonly DependencyProperty BarHeightProperty = RenderProperty(nameof(BarHeight), double.NaN);

    /// <summary>Identifies the <see cref="BeatTop"/> dependency property.</summary>
    public static readonly DependencyProperty BeatTopProperty = RenderProperty(nameof(BeatTop), 0.0);

    /// <summary>Identifies the <see cref="BarTop"/> dependency property.</summary>
    public static readonly DependencyProperty BarTopProperty = RenderProperty(nameof(BarTop), 0.0);

    /// <summary>Tempo of the grid (beats per minute).</summary>
    public double Bpm
    {
        get => (double)GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    /// <summary>Show time of a beat (usually the first beat of the music); bars are counted from it.</summary>
    public double BeatOffsetSeconds
    {
        get => (double)GetValue(BeatOffsetSecondsProperty);
        set => SetValue(BeatOffsetSecondsProperty, value);
    }

    public Brush? BeatBrush
    {
        get => (Brush?)GetValue(BeatBrushProperty);
        set => SetValue(BeatBrushProperty, value);
    }

    /// <summary>Brush of bar marks; the beat brush when null.</summary>
    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public double BeatWidth
    {
        get => (double)GetValue(BeatWidthProperty);
        set => SetValue(BeatWidthProperty, value);
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    /// <summary>Height of a beat mark; NaN = the element's full height.</summary>
    public double BeatHeight
    {
        get => (double)GetValue(BeatHeightProperty);
        set => SetValue(BeatHeightProperty, value);
    }

    /// <summary>Height of a bar mark; NaN = the element's full height.</summary>
    public double BarHeight
    {
        get => (double)GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    public double BeatTop
    {
        get => (double)GetValue(BeatTopProperty);
        set => SetValue(BeatTopProperty, value);
    }

    public double BarTop
    {
        get => (double)GetValue(BarTopProperty);
        set => SetValue(BarTopProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var pps = SafePixelsPerSecond;
        if (pps <= 0 || !TryBeginRender(out var from, out var to))
        {
            return;
        }

        var bpm = Bpm;
        var beatPixels = TimelineSnap.BeatSeconds(bpm) * pps;
        var stride = beatPixels >= MinBeatSpacing ? 1
            : beatPixels * TimelineSnap.BeatsPerBar >= MinBeatSpacing ? TimelineSnap.BeatsPerBar
            : 0;
        if (stride == 0)
        {
            return;
        }

        var beatBrush = BeatBrush;
        var barBrush = BarBrush ?? beatBrush;
        var fullHeight = ActualHeight;
        var beat = (Width: BeatWidth, Height: Resolve(BeatHeight, fullHeight), Top: BeatTop);
        var bar = (Width: BarWidth, Height: Resolve(BarHeight, fullHeight), Top: BarTop);
        foreach (var line in TimelineSnap.BeatLines(from / pps, to / pps, bpm, BeatOffsetSeconds, stride))
        {
            var brush = line.IsBarStart ? barBrush : beatBrush;
            var mark = line.IsBarStart ? bar : beat;
            if (brush is null || mark.Height <= 0 || mark.Width <= 0)
            {
                continue;
            }

            // Whole pixels keep 1 px lines sharp.
            drawingContext.DrawRectangle(brush, null, new Rect(Math.Round(line.Seconds * pps), mark.Top, mark.Width, mark.Height));
        }
    }

    private static double Resolve(double height, double fullHeight) => double.IsNaN(height) ? fullHeight : height;

    private static DependencyProperty RenderProperty<T>(string name, T defaultValue) => DependencyProperty.Register(
        name, typeof(T), typeof(TimelineBeatMarks),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
