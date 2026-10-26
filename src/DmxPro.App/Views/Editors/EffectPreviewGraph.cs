using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DmxPro.App.Controls;
using DmxPro.App.ViewModels.Editors;

namespace DmxPro.App.Views.Editors;

/// <summary>
/// On-screen preview of a pulse or strobe: a lamp (left) in the light colour and the brightness curve over time
/// (right; bottom = off, top = full). With <see cref="IsAnimated"/> a marker runs along the curve over
/// <see cref="WindowSeconds"/> and the lamp follows it — only while the control is visible and Windows animations are
/// on (<see cref="DmxMotion.IsEnabled"/>); otherwise the lamp shows the brightest level and nothing moves. Purely a view
/// concern: the curve comes from the ViewModel (<see cref="EffectPreviewMath"/>).
/// </summary>
public sealed class EffectPreviewGraph : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IReadOnlyList<EffectPreviewPoint>),
        typeof(EffectPreviewGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LightColorProperty = DependencyProperty.Register(
        nameof(LightColor),
        typeof(string),
        typeof(EffectPreviewGraph),
        new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.AffectsRender, OnLightColorChanged));

    public static readonly DependencyProperty WindowSecondsProperty = DependencyProperty.Register(
        nameof(WindowSeconds),
        typeof(double),
        typeof(EffectPreviewGraph),
        new FrameworkPropertyMetadata(1.0));

    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated),
        typeof(bool),
        typeof(EffectPreviewGraph),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnAnimationInputChanged));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(EffectPreviewGraph),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender, OnPenInputChanged));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush),
        typeof(Brush),
        typeof(EffectPreviewGraph),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender, OnPenInputChanged));

    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
        nameof(MarkerBrush),
        typeof(Brush),
        typeof(EffectPreviewGraph),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender, OnPenInputChanged));

    // A real light colour: the lamp when it is off.
    private static readonly Brush LampOffBrush = CreateFrozen(Color.FromRgb(0, 0, 0));

    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer;
    private Brush _lightBrush = CreateFrozen(Colors.White);
    private Brush _fillBrush = CreateFrozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
    private Pen? _linePen;
    private Pen? _gridPen;
    private Pen? _markerPen;

    public EffectPreviewGraph()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => UpdateTimer();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    /// <summary>The curve; X and Level 0..1.</summary>
    public IReadOnlyList<EffectPreviewPoint>? Points
    {
        get => (IReadOnlyList<EffectPreviewPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>"#RRGGBB" light colour of the lamp and the area under the curve.</summary>
    public string? LightColor
    {
        get => (string?)GetValue(LightColorProperty);
        set => SetValue(LightColorProperty, value);
    }

    /// <summary>Seconds the whole curve stands for (the marker crosses it in this time).</summary>
    public double WindowSeconds
    {
        get => (double)GetValue(WindowSecondsProperty);
        set => SetValue(WindowSecondsProperty, value);
    }

    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush MarkerBrush
    {
        get => (Brush)GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    private bool IsRunning => _timer.IsEnabled;

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 64 : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 40 || height < 16)
        {
            return;
        }

        _linePen ??= CreatePen(LineBrush, 1.5);
        _gridPen ??= CreatePen(GridBrush, 1);
        _markerPen ??= CreatePen(MarkerBrush, 1.5);

        var points = Points ?? [];
        var lampSize = Math.Min(height, 48);
        var graphLeft = lampSize + 10;
        var graphWidth = width - graphLeft;
        var top = 2.0;
        var bottom = height - 2;
        var graphHeight = bottom - top;

        // Where the marker is (animated) and how bright the lamp is.
        double? markerX = null;
        double lampLevel;
        if (IsRunning && WindowSeconds > 0 && double.IsFinite(WindowSeconds))
        {
            var t = (_clock.Elapsed.TotalSeconds % WindowSeconds) / WindowSeconds;
            markerX = t;
            lampLevel = EffectPreviewMath.LevelAt(points, t);
        }
        else
        {
            lampLevel = EffectPreviewMath.PeakLevel(points);
        }

        // Lamp: black (off) with the light colour on top at the current level.
        var lampCenter = new Point(lampSize / 2, height / 2);
        var radius = (lampSize / 2) - 1;
        drawingContext.DrawEllipse(LampOffBrush, _gridPen, lampCenter, radius, radius);
        drawingContext.PushOpacity(Math.Clamp(lampLevel, 0, 1));
        drawingContext.DrawEllipse(_lightBrush, null, lampCenter, radius - 1, radius - 1);
        drawingContext.Pop();

        // Frame: the "off" baseline and the "full" line.
        drawingContext.DrawLine(_gridPen, new Point(graphLeft, bottom), new Point(width, bottom));
        drawingContext.DrawLine(_gridPen, new Point(graphLeft, top), new Point(width, top));

        if (points.Count >= 2 && graphWidth > 0)
        {
            var curve = new StreamGeometry();
            var area = new StreamGeometry();
            using (var c = curve.Open())
            using (var a = area.Open())
            {
                var first = ToScreen(points[0]);
                c.BeginFigure(first, isFilled: false, isClosed: false);
                a.BeginFigure(new Point(first.X, bottom), isFilled: true, isClosed: true);
                a.LineTo(first, isStroked: false, isSmoothJoin: false);
                for (var i = 1; i < points.Count; i++)
                {
                    var p = ToScreen(points[i]);
                    c.LineTo(p, isStroked: true, isSmoothJoin: true);
                    a.LineTo(p, isStroked: false, isSmoothJoin: false);
                }

                a.LineTo(new Point(ToScreen(points[^1]).X, bottom), isStroked: false, isSmoothJoin: false);
            }

            curve.Freeze();
            area.Freeze();
            drawingContext.DrawGeometry(_fillBrush, null, area);
            drawingContext.DrawGeometry(null, _linePen, curve);
        }

        if (markerX is { } mx)
        {
            var x = graphLeft + (mx * graphWidth);
            drawingContext.DrawLine(_markerPen, new Point(x, top), new Point(x, bottom));
        }

        Point ToScreen(EffectPreviewPoint p) =>
            new(graphLeft + (Math.Clamp(p.X, 0, 1) * graphWidth), bottom - (Math.Clamp(p.Level, 0, 1) * graphHeight));
    }

    private static void OnLightColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (EffectPreviewGraph)d;
        var color = Colors.White;
        if (e.NewValue is string hex && ColorConverter.ConvertFromString(hex) is Color parsed)
        {
            color = parsed;
        }

        graph._lightBrush = CreateFrozen(color);
        graph._fillBrush = CreateFrozen(Color.FromArgb(0x70, color.R, color.G, color.B));
    }

    private static void OnAnimationInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EffectPreviewGraph)d).UpdateTimer();

    private static void OnPenInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (EffectPreviewGraph)d;
        graph._linePen = null;
        graph._gridPen = null;
        graph._markerPen = null;
    }

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        if (brush.IsFrozen)
        {
            pen.Freeze();
        }

        return pen;
    }

    private void UpdateTimer()
    {
        var run = IsAnimated && IsLoaded && IsVisible && DmxMotion.IsEnabled;
        if (run && !_timer.IsEnabled)
        {
            _clock.Restart();
            _timer.Start();
        }
        else if (!run && _timer.IsEnabled)
        {
            _timer.Stop();
            _clock.Reset();
        }

        InvalidateVisual();
    }
}
