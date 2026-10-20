using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DmxPro.App.Controls;

/// <summary>
/// Big console fader: the whole widget is the fader. Fills from the bottom (or from the left when wider than tall)
/// with <see cref="FillBrush"/>. Drag, click, mouse wheel and arrow/Page/Home/End keys change the value (0..1).
/// </summary>
public sealed class ConsoleFader : RangeBase
{
    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LineThickness = 3;
    private const double KeyStep = 0.05;
    private const double PageStep = 0.25;

    static ConsoleFader()
    {
        MaximumProperty.OverrideMetadata(typeof(ConsoleFader), new FrameworkPropertyMetadata(1.0));
        FocusableProperty.OverrideMetadata(typeof(ConsoleFader), new FrameworkPropertyMetadata(true));
        ValueProperty.OverrideMetadata(
            typeof(ConsoleFader),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    /// <summary>Fill colour of the level part.</summary>
    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>Background of the empty part.</summary>
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>The level line drawn at the top (or right) edge of the fill.</summary>
    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private bool IsVertical => ActualHeight >= ActualWidth;

    protected override AutomationPeer OnCreateAutomationPeer() => new FaderAutomationPeer(this);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var radius = Math.Min(CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);
        drawingContext.DrawRoundedRectangle(TrackBrush ?? Brushes.Transparent, null, bounds, radius, radius);

        var fraction = Fraction();
        drawingContext.PushClip(new RectangleGeometry(bounds, radius, radius));
        if (IsVertical)
        {
            var top = bounds.Height * (1 - fraction);
            var lineTop = Math.Clamp(top - (LineThickness / 2), 0, bounds.Height - LineThickness);
            drawingContext.DrawRectangle(FillBrush, null, new Rect(0, top, bounds.Width, bounds.Height - top));
            drawingContext.DrawRectangle(LineBrush, null, new Rect(0, lineTop, bounds.Width, LineThickness));
        }
        else
        {
            var right = bounds.Width * fraction;
            var lineLeft = Math.Clamp(right - (LineThickness / 2), 0, bounds.Width - LineThickness);
            drawingContext.DrawRectangle(FillBrush, null, new Rect(0, 0, right, bounds.Height));
            drawingContext.DrawRectangle(LineBrush, null, new Rect(lineLeft, 0, LineThickness, bounds.Height));
        }

        drawingContext.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            SetFromPoint(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Step(e.Delta > 0 ? KeyStep : -KeyStep);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Up or Key.Right:
                Step(KeyStep);
                break;
            case Key.Down or Key.Left:
                Step(-KeyStep);
                break;
            case Key.PageUp:
                Step(PageStep);
                break;
            case Key.PageDown:
                Step(-PageStep);
                break;
            case Key.Home:
                Value = Minimum;
                break;
            case Key.End:
                Value = Maximum;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    private double Fraction()
    {
        var range = Maximum - Minimum;
        return range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
    }

    private void Step(double delta) => Value = Math.Clamp(Value + (delta * (Maximum - Minimum)), Minimum, Maximum);

    private void SetFromPoint(Point point)
    {
        var fraction = IsVertical
            ? 1 - (point.Y / Math.Max(1, ActualHeight))
            : point.X / Math.Max(1, ActualWidth);
        Value = Minimum + (Math.Clamp(fraction, 0, 1) * (Maximum - Minimum));
    }

    private sealed class FaderAutomationPeer(ConsoleFader owner) : RangeBaseAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(ConsoleFader);
    }
}
