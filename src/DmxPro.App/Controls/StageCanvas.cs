using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Controls;

/// <summary>
/// The virtual stage plan of the Stage page: a dark stage floor with a metre grid, every placed light drawn as a fixture
/// body with a glow and a short downstage beam in its live colour, selection, names, and mouse/keyboard editing.
/// </summary>
/// <remarks>
/// <para><b>Rendering.</b> One <see cref="ContainerVisual"/> ("world", panned by a single transform) holds the floor and
/// two layers of <see cref="DrawingVisual"/>s: all glows below all bodies (see <see cref="StageLightVisual"/>). No
/// UIElement per light and no layout pass. A frame only changes brush colours (no re-recording, no allocations); zoom,
/// names and selection re-record the affected drawings; a move or pan changes one transform.</para>
/// <para><b>Coordinates.</b> Stage metres: X from the left edge, Y from the front (downstage) edge. On screen the
/// audience is at the bottom, so world pixels are (X·scale, (Depth − Y)·scale) from the stage's top-left corner.</para>
/// <para><b>Mouse.</b> Click selects (empty floor clears the selection); dragging a light moves it, snapped to
/// <see cref="StagePageViewModel.SnapStep"/> unless Shift is held (free move); Esc while dragging puts it back. Wheel
/// zooms around the cursor; middle or right drag pans. Lights dragged from the lights list (data format
/// <see cref="LightDragFormat"/>, a fixture id) are placed where they are dropped.</para>
/// <para><b>Keys</b> (when focused). Arrows nudge the selected light by the snap step (Shift: 0.05 m, Ctrl: 1 m; Up =
/// upstage); Delete takes it off the stage; Home fits the stage into view; Esc clears the selection.</para>
/// </remarks>
public sealed class StageCanvas : FrameworkElement
{
    /// <summary>Drag-and-drop data format for a light from the lights list (data: the fixture <see cref="Guid"/>).</summary>
    public const string LightDragFormat = "DmxPro.StageLight";

    /// <summary>Identifies the <see cref="Stage"/> dependency property.</summary>
    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage),
        typeof(StagePageViewModel),
        typeof(StageCanvas),
        new FrameworkPropertyMetadata(null, OnStageChanged));

    /// <summary>Identifies the <see cref="Foreground"/> dependency property (text outside the floor, e.g. "Audience").</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(StageCanvas),
        new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits, OnLookChanged));

    /// <summary>Identifies the <see cref="SelectionBrush"/> dependency property.</summary>
    public static readonly DependencyProperty SelectionBrushProperty = DependencyProperty.Register(
        nameof(SelectionBrush),
        typeof(Brush),
        typeof(StageCanvas),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, OnLookChanged));

    /// <summary>Free space around the fitted stage, in pixels.</summary>
    private const double FitMargin = 16;

    /// <summary>Room under the front edge for the "Audience" label.</summary>
    private const double AudienceBand = 22;

    private const double MaxScale = 400;
    private const double AbsoluteMinScale = 1;
    private const double ZoomStep = 1.15;
    private const double FineNudge = 0.05;
    private const double CoarseNudge = 1;

    // The floor stays dark in both themes, like a real stage, so the coloured light reads the same everywhere.
    private static readonly Brush FloorBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x17, 0x19, 0x1D)));
    private static readonly Pen FloorEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x3B, 0x40, 0x48))), 1));
    private static readonly Pen FrontEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x8C, 0x93, 0x9E))), 3));
    private static readonly Pen MetrePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen FiveMetrePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly ContainerVisual _world = new();
    private readonly TranslateTransform _pan = new();
    private readonly DrawingVisual _floor = new();
    private readonly ContainerVisual _glowLayer = new();
    private readonly ContainerVisual _bodyLayer = new();
    private readonly List<StageLightVisual> _visuals = [];
    private readonly Dictionary<StageLightViewModel, StageLightVisual> _byLight = new(ReferenceEqualityComparer.Instance);

    private StagePageViewModel? _attached;
    private double _scale = 40;
    private bool _needsFit = true;
    private StageLightVisual? _selectedVisual;

    // Pointer gesture state.
    private Gesture _gesture;
    private StageLightVisual? _dragVisual;
    private Point _pressPoint;
    private Point _lastPanPoint;
    private Vector _grabOffset;
    private double _dragOriginX;
    private double _dragOriginY;

    static StageCanvas()
    {
        FocusableProperty.OverrideMetadata(typeof(StageCanvas), new FrameworkPropertyMetadata(true));
        ClipToBoundsProperty.OverrideMetadata(typeof(StageCanvas), new FrameworkPropertyMetadata(true));
        AllowDropProperty.OverrideMetadata(typeof(StageCanvas), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Initializes a new instance of the <see cref="StageCanvas"/> class.</summary>
    public StageCanvas()
    {
        _world.Transform = _pan;
        _world.Children.Add(_floor);
        _world.Children.Add(_glowLayer);
        _world.Children.Add(_bodyLayer);
        AddVisualChild(_world);
        Loaded += (_, _) => Attach(Stage);
        Unloaded += (_, _) => Attach(null);
    }

    private enum Gesture
    {
        None,
        Pressed,
        Moving,
        Panning,
    }

    /// <summary>Gets or sets the Stage page whose lights are drawn and edited.</summary>
    public StagePageViewModel? Stage
    {
        get => (StagePageViewModel?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    /// <summary>Gets or sets the brush of the text around the floor. Inherits.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Gets or sets the brush of the selection outline (the accent colour).</summary>
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    private double StageWidth => Math.Max(_attached?.StageWidth ?? 12, StagePageViewModel.MinStageSize);

    private double StageDepth => Math.Max(_attached?.StageDepth ?? 8, StagePageViewModel.MinStageSize);

    /// <summary>Zooms and centres so the whole stage fits the control.</summary>
    public void FitToView()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            _needsFit = true;
            return;
        }

        _needsFit = false;
        var scale = Math.Min(
            (width - (2 * FitMargin)) / StageWidth,
            (height - (2 * FitMargin) - AudienceBand) / StageDepth);
        _scale = Math.Clamp(scale, AbsoluteMinScale, MaxScale);
        _pan.X = Math.Round((width - (StageWidth * _scale)) / 2);
        _pan.Y = Math.Round((height - AudienceBand - (StageDepth * _scale)) / 2);
        RenderAll();
    }

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _world : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc />
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        // Transparent background so empty areas still get mouse and drop events.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_needsFit)
        {
            FitToView();
        }
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        RenderAll();
    }

    // ───────────────────────────── ViewModel wiring ─────────────────────────────

    private static void OnStageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (StageCanvas)d;
        if (canvas.IsLoaded)
        {
            canvas.Attach((StagePageViewModel?)e.NewValue);
        }
    }

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StageCanvas)d).RenderAll();

    private void Attach(StagePageViewModel? stage)
    {
        if (ReferenceEquals(stage, _attached))
        {
            return;
        }

        if (_attached is { } old)
        {
            old.FrameUpdated -= OnFrameUpdated;
            old.SceneChanged -= OnSceneChanged;
            old.PlacementsChanged -= OnPlacementsChanged;
            old.SelectionChanged -= OnSelectionChanged;
            old.FitRequested -= OnFitRequested;
        }

        _attached = stage;
        if (stage is not null)
        {
            stage.FrameUpdated += OnFrameUpdated;
            stage.SceneChanged += OnSceneChanged;
            stage.PlacementsChanged += OnPlacementsChanged;
            stage.SelectionChanged += OnSelectionChanged;
            stage.FitRequested += OnFitRequested;
        }

        RebuildScene();
    }

    private void OnFrameUpdated(object? sender, EventArgs e)
    {
        // Hot path (~30 Hz): colours only. Plain for loop over a List: no enumerator boxing, no allocations.
        for (var i = 0; i < _visuals.Count; i++)
        {
            var visual = _visuals[i];
            if (visual.IsShown)
            {
                visual.UpdateColor();
            }
        }
    }

    private void OnSceneChanged(object? sender, EventArgs e) => RebuildScene();

    private void OnPlacementsChanged(object? sender, EventArgs e) => SyncPlacements();

    private void OnFitRequested(object? sender, EventArgs e) => FitToView();

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var old = _selectedVisual;
        _selectedVisual = _attached?.SelectedLight is { } light && _byLight.TryGetValue(light, out var visual) ? visual : null;
        if (old is not null)
        {
            Render(old);
        }

        if (_selectedVisual is { } selected && !ReferenceEquals(selected, old))
        {
            Render(selected);

            // Keep the selected light on top of its neighbours.
            if (selected.IsShown)
            {
                _bodyLayer.Children.Remove(selected.Body);
                _bodyLayer.Children.Add(selected.Body);
            }
        }
    }

    /// <summary>Creates the visuals for the current lights (after a patch change or project open).</summary>
    private void RebuildScene()
    {
        CancelGesture();
        _glowLayer.Children.Clear();
        _bodyLayer.Children.Clear();
        _visuals.Clear();
        _byLight.Clear();
        _selectedVisual = null;
        if (_attached is { } stage)
        {
            foreach (var light in stage.DrawableLights)
            {
                var visual = new StageLightVisual(light);
                _visuals.Add(visual);
                _byLight.Add(light, visual);
            }

            _selectedVisual = stage.SelectedLight is { } selected && _byLight.TryGetValue(selected, out var v) ? v : null;
        }

        SyncPlacements();
        RenderAll();
        OnFrameUpdated(this, EventArgs.Empty);
    }

    /// <summary>Shows placed lights, hides unplaced ones and moves every light to its position.</summary>
    private void SyncPlacements()
    {
        foreach (var visual in _visuals)
        {
            var placed = visual.Light.IsPlaced;
            if (placed && !visual.IsShown)
            {
                _glowLayer.Children.Add(visual.Glow);
                _bodyLayer.Children.Add(visual.Body);
                visual.IsShown = true;
                visual.UpdateColor();
            }
            else if (!placed && visual.IsShown)
            {
                _glowLayer.Children.Remove(visual.Glow);
                _bodyLayer.Children.Remove(visual.Body);
                visual.IsShown = false;
            }

            Position(visual);
        }
    }

    private void Position(StageLightVisual visual)
    {
        visual.Position.X = visual.Light.X * _scale;
        visual.Position.Y = (StageDepth - visual.Light.Y) * _scale;
    }

    // ───────────────────────────── Rendering ─────────────────────────────

    /// <summary>Re-records the floor and every light (zoom, names, theme or scene change; never per frame).</summary>
    private void RenderAll()
    {
        RenderFloor();
        foreach (var visual in _visuals)
        {
            Render(visual);
            Position(visual);
        }
    }

    private void Render(StageLightVisual visual)
    {
        Pen? selection = null;
        if (ReferenceEquals(visual, _selectedVisual) && SelectionBrush is { } brush)
        {
            selection = new Pen(brush, 2);
            if (selection.CanFreeze)
            {
                selection.Freeze();
            }
        }

        visual.Render(_scale, _attached?.ShowNames ?? true, selection, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private void RenderFloor()
    {
        var width = StageWidth * _scale;
        var depth = StageDepth * _scale;
        using var dc = _floor.RenderOpen();
        dc.DrawRectangle(FloorBrush, FloorEdgePen, new Rect(0, 0, width, depth));

        // Metre grid; at low zoom only every 5 m (lines closer than 6 px are noise).
        var step = _scale >= 6 ? 1 : _scale * 5 >= 6 ? 5 : 0;
        if (step > 0)
        {
            for (var x = step; x < StageWidth; x += step)
            {
                var px = Math.Round(x * _scale) + 0.5;
                dc.DrawLine(x % 5 == 0 ? FiveMetrePen : MetrePen, new Point(px, 0), new Point(px, depth));
            }

            // From the front edge, so whole metres from downstage line up with the grid.
            for (var y = step; y < StageDepth; y += step)
            {
                var py = Math.Round(depth - (y * _scale)) + 0.5;
                dc.DrawLine(y % 5 == 0 ? FiveMetrePen : MetrePen, new Point(0, py), new Point(width, py));
            }
        }

        dc.DrawLine(FrontEdgePen, new Point(0, depth), new Point(width, depth));

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var foreground = Foreground ?? Brushes.Gray;
        var audience = new FormattedText("AUDIENCE", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelTypeface, 11, foreground, pixelsPerDip);
        dc.DrawText(audience, new Point((width - audience.Width) / 2, depth + 5));

        var size = new FormattedText(
            string.Create(CultureInfo.CurrentCulture, $"{StageWidth:0.##} m × {StageDepth:0.##} m"),
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            foreground,
            pixelsPerDip);
        dc.DrawText(size, new Point(width - size.Width, depth + 6));
    }

    // ───────────────────────────── Coordinates / hit testing ─────────────────────────────

    /// <summary>Screen point → stage metres (not clamped).</summary>
    private Point ToStage(Point screen) =>
        new((screen.X - _pan.X) / _scale, StageDepth - ((screen.Y - _pan.Y) / _scale));

    private Point ToScreen(double x, double y) =>
        new((x * _scale) + _pan.X, ((StageDepth - y) * _scale) + _pan.Y);

    /// <summary>The placed light nearest to the point within its grab radius (topmost wins on a tie), or null.</summary>
    private StageLightVisual? HitLight(Point screen)
    {
        var radius = Math.Max(StageLightVisual.BodyRadius(_scale) + 4, 10);
        var best = radius * radius;
        StageLightVisual? hit = null;

        // Selected light first: it is drawn on top.
        if (_selectedVisual is { IsShown: true } selected && Distance2(selected, screen) <= best)
        {
            return selected;
        }

        for (var i = _visuals.Count - 1; i >= 0; i--)
        {
            var visual = _visuals[i];
            if (!visual.IsShown)
            {
                continue;
            }

            var d2 = Distance2(visual, screen);
            if (d2 < best)
            {
                best = d2;
                hit = visual;
            }
        }

        return hit;
    }

    private double Distance2(StageLightVisual visual, Point screen)
    {
        var centre = ToScreen(visual.Light.X, visual.Light.Y);
        var dx = centre.X - screen.X;
        var dy = centre.Y - screen.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double Snap(double metres) =>
        Math.Round(metres / StagePageViewModel.SnapStep) * StagePageViewModel.SnapStep;

    // ───────────────────────────── Mouse ─────────────────────────────

    /// <inheritdoc />
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_gesture != Gesture.None || _attached is not { } stage)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Left)
        {
            var hit = HitLight(point);
            stage.SelectedLight = hit?.Light;
            if (hit is not null)
            {
                _gesture = Gesture.Pressed;
                _dragVisual = hit;
                _pressPoint = point;
                _dragOriginX = hit.Light.X;
                _dragOriginY = hit.Light.Y;
                _grabOffset = ToStage(point) - new Point(hit.Light.X, hit.Light.Y);
                CaptureMouse();
            }

            e.Handled = true;
        }
        else if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _gesture = Gesture.Panning;
            _lastPanPoint = point;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        switch (_gesture)
        {
            case Gesture.Pressed:
                if (Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _gesture = Gesture.Moving;
                Cursor = Cursors.SizeAll;
                MoveDragged(point);
                break;
            case Gesture.Moving:
                MoveDragged(point);
                break;
            case Gesture.Panning:
                _pan.X += point.X - _lastPanPoint.X;
                _pan.Y += point.Y - _lastPanPoint.Y;
                _lastPanPoint = point;
                break;
            default:
                Cursor = HitLight(point) is null ? null : Cursors.Hand;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        var ends = _gesture switch
        {
            Gesture.Pressed or Gesture.Moving => e.ChangedButton == MouseButton.Left,
            Gesture.Panning => e.ChangedButton is MouseButton.Middle or MouseButton.Right,
            _ => false,
        };
        if (ends)
        {
            // Lost-capture handling finishes the gesture (commit of a move).
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_gesture == Gesture.Moving)
        {
            _attached?.CommitMove();
        }

        _gesture = Gesture.None;
        _dragVisual = null;
        Cursor = null;
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var factor = Math.Pow(ZoomStep, e.Delta / 120.0);
        ZoomAt(e.GetPosition(this), factor);
        e.Handled = true;
    }

    private void MoveDragged(Point screen)
    {
        if (_dragVisual is not { } visual || _attached is not { } stage)
        {
            return;
        }

        var target = ToStage(screen) - _grabOffset;
        var free = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        stage.MoveLight(visual.Light, free ? target.X : Snap(target.X), free ? target.Y : Snap(target.Y));
        Position(visual);
    }

    private void ZoomAt(Point screen, double factor)
    {
        var fit = Math.Min(Math.Max(ActualWidth, 1) / StageWidth, Math.Max(ActualHeight, 1) / StageDepth);
        var scale = Math.Clamp(_scale * factor, Math.Max(AbsoluteMinScale, fit * 0.2), MaxScale);
        if (scale == _scale)
        {
            return;
        }

        // Keep the stage point under the cursor where it is.
        var k = scale / _scale;
        _pan.X = screen.X - ((screen.X - _pan.X) * k);
        _pan.Y = screen.Y - ((screen.Y - _pan.Y) * k);
        _scale = scale;
        _needsFit = false;
        RenderAll();
    }

    /// <summary>Ends any gesture without committing: a light being dragged goes back to where it was.</summary>
    private void CancelGesture()
    {
        if (_gesture == Gesture.Moving && _dragVisual is { } visual && _attached is { } stage)
        {
            stage.CancelMove(visual.Light, _dragOriginX, _dragOriginY);
            Position(visual);
        }

        _gesture = Gesture.None;
        _dragVisual = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    // ───────────────────────────── Keyboard ─────────────────────────────

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_attached is not { } stage)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var step = modifiers.HasFlag(ModifierKeys.Control) ? CoarseNudge
            : modifiers.HasFlag(ModifierKeys.Shift) ? FineNudge
            : StagePageViewModel.SnapStep;
        switch (e.Key)
        {
            case Key.Left:
                stage.NudgeSelected(-step, 0);
                break;
            case Key.Right:
                stage.NudgeSelected(step, 0);
                break;
            case Key.Up:
                stage.NudgeSelected(0, step);
                break;
            case Key.Down:
                stage.NudgeSelected(0, -step);
                break;
            case Key.Delete:
                if (stage.RemoveFromStageCommand.CanExecute(null))
                {
                    stage.RemoveFromStageCommand.Execute(null);
                }

                break;
            case Key.Home:
                FitToView();
                break;
            case Key.Escape:
                if (_gesture == Gesture.Moving)
                {
                    CancelGesture();
                }
                else
                {
                    stage.SelectedLight = null;
                }

                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ───────────────────────────── Drop from the lights list ─────────────────────────────

    /// <inheritdoc />
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(LightDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (_attached is not { } stage || e.Data.GetData(LightDragFormat) is not Guid fixtureId)
        {
            return;
        }

        var point = ToStage(e.GetPosition(this));
        var free = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;
        stage.PlaceLight(fixtureId, free ? point.X : Snap(point.X), free ? point.Y : Snap(point.Y));
        Focus();
        e.Handled = true;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
