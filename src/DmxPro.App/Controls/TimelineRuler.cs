using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DmxPro.App.Controls;

/// <summary>
/// The Shows timeline's time ruler: a tick and a "mm:ss" label every few seconds (the spacing grows when zoomed out so
/// labels never crowd), drawn in one pass and only around the visible part (see <see cref="TimelineViewportElement"/>).
/// Replaces a templated element per tick, which meant thousands of text blocks for a long show.
/// </summary>
public sealed class TimelineRuler : TimelineViewportElement
{
    /// <summary>Label spacings (seconds); the first giving at least <see cref="MinTickSpacing"/> pixels is used.</summary>
    private static readonly double[] TickSteps = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600];

    private const double MinTickSpacing = 64;
    private const double TickHeight = 5;
    private const double LabelLeft = 3;
    private const double LabelTop = 1;

    /// <summary>Identifies the <see cref="TickBrush"/> dependency property.</summary>
    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(
        nameof(TickBrush), typeof(Brush), typeof(TimelineRuler),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="Foreground"/> dependency property (label colour).</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(TimelineRuler),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Identifies the <see cref="FontSize"/> dependency property.</summary>
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(TimelineRuler),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public Brush? TickBrush
    {
        get => (Brush?)GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>"mm:ss", or "mm:ss.t" for steps below a second.</summary>
    public static string FormatLabel(double seconds, bool tenths)
    {
        var deciseconds = (long)Math.Round(Math.Max(0, seconds) * 10);
        var whole = deciseconds / 10;
        return tenths
            ? string.Create(CultureInfo.InvariantCulture, $"{whole / 60:00}:{whole % 60:00}.{deciseconds % 10}")
            : string.Create(CultureInfo.InvariantCulture, $"{whole / 60:00}:{whole % 60:00}");
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var pps = SafePixelsPerSecond;
        if (pps <= 0 || !TryBeginRender(out var from, out var to))
        {
            return;
        }

        var step = TickSteps[^1];
        foreach (var candidate in TickSteps)
        {
            if (candidate * pps >= MinTickSpacing)
            {
                step = candidate;
                break;
            }
        }

        var tickBrush = TickBrush;
        var labelBrush = Foreground ?? Brushes.Gray;
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var fontSize = FontSize > 0 ? FontSize : SystemFonts.MessageFontSize;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tickTop = ActualHeight - TickHeight;
        var tenths = step < 1;

        // Start one label early so a label whose tick is just left of the window still shows its text.
        var first = Math.Max(0, (long)Math.Floor((from - MinTickSpacing) / pps / step));
        for (var i = first; ; i++)
        {
            var seconds = i * step;
            var x = Math.Round(seconds * pps);
            if (x > to)
            {
                break;
            }

            if (tickBrush is not null)
            {
                drawingContext.DrawRectangle(tickBrush, null, new Rect(x, tickTop, 1, TickHeight));
            }

            var text = new FormattedText(
                FormatLabel(seconds, tenths), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, labelBrush, pixelsPerDip);
            drawingContext.DrawText(text, new Point(x + LabelLeft, LabelTop));
        }
    }
}
