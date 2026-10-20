using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DmxPro.App.Controls;

/// <summary>
/// Draws one of the app's vector icons (see <c>Themes/Icons.xaml</c>, generated from <c>Assets/Icons/*.svg</c>).
/// The 24×24 icon geometry is scaled uniformly into the element and filled with <see cref="Foreground"/>, which
/// inherits from the parent (e.g. a button), so hover and disabled states follow the host control automatically.
/// </summary>
/// <example>
/// <code>
/// xmlns:c="clr-namespace:DmxPro.App.Controls"
/// &lt;c:DmxIcon Kind="Save" Size="20" /&gt;
/// </code>
/// </example>
public sealed class DmxIcon : FrameworkElement
{
    /// <summary>Side length of the source icon grid; every icon geometry is authored in a 0 0 24 24 box.</summary>
    private const double SourceSize = 24.0;

    /// <summary>Prefix of the icon resource keys in <c>Themes/Icons.xaml</c>.</summary>
    private const string ResourceKeyPrefix = "Icon.";

    /// <summary>Identifies the <see cref="Kind"/> dependency property.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(string),
        typeof(DmxIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnKindChanged));

    /// <summary>Identifies the <see cref="Data"/> dependency property.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(Geometry),
        typeof(DmxIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="Size"/> dependency property.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(DmxIcon),
        new FrameworkPropertyMetadata(20.0, OnSizeChanged),
        IsValidSize);

    /// <summary>
    /// Identifies the <c>MaxSize</c> attached property: an inherited upper bound for <see cref="Size"/>. Compact host
    /// styles (toolbar buttons, empty states) set it once so every icon inside stays at the design-system size, even when
    /// a view still asks for a larger <see cref="Size"/>.
    /// </summary>
    public static readonly DependencyProperty MaxSizeProperty = DependencyProperty.RegisterAttached(
        "MaxSize",
        typeof(double),
        typeof(DmxIcon),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.Inherits, OnSizeChanged),
        IsValidMaxSize);

    /// <summary>Identifies the <see cref="Foreground"/> dependency property (shared with <see cref="TextElement"/>).</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(DmxIcon),
        new FrameworkPropertyMetadata(
            SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private Geometry? _resolvedGeometry;

    static DmxIcon()
    {
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(DmxIcon), new FrameworkPropertyMetadata(true));
        UseLayoutRoundingProperty.OverrideMetadata(typeof(DmxIcon), new FrameworkPropertyMetadata(true));
        FocusableProperty.OverrideMetadata(typeof(DmxIcon), new FrameworkPropertyMetadata(false));
        WidthProperty.OverrideMetadata(typeof(DmxIcon), new FrameworkPropertyMetadata(20.0));
        HeightProperty.OverrideMetadata(typeof(DmxIcon), new FrameworkPropertyMetadata(20.0));
    }

    /// <summary>Initializes a new instance of the <see cref="DmxIcon"/> class.</summary>
    public DmxIcon()
    {
        Loaded += (_, _) => ResolveKind();
    }

    /// <summary>
    /// Gets or sets the icon name, e.g. <c>"Save"</c>; resolved as resource <c>Icon.&lt;Kind&gt;</c>.
    /// Ignored when <see cref="Data"/> is set.
    /// </summary>
    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Gets or sets a geometry (in a 24×24 box) to draw directly; takes precedence over <see cref="Kind"/>.</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Gets or sets the icon edge length in device-independent pixels (sets Width and Height). Default 20.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Gets the inherited upper bound for the icon size (see <see cref="MaxSizeProperty"/>).</summary>
    /// <param name="element">The element to read from.</param>
    /// <returns>The maximum icon edge length in device-independent pixels.</returns>
    public static double GetMaxSize(DependencyObject element) => (double)element.GetValue(MaxSizeProperty);

    /// <summary>Sets the inherited upper bound for the icon size (see <see cref="MaxSizeProperty"/>).</summary>
    /// <param name="element">The element to set it on; icons below it inherit the value.</param>
    /// <param name="value">The maximum icon edge length in device-independent pixels.</param>
    public static void SetMaxSize(DependencyObject element, double value) => element.SetValue(MaxSizeProperty, value);

    /// <summary>Gets or sets the fill brush. Inherits from the parent element's foreground.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var geometry = Data ?? _resolvedGeometry;
        var brush = Foreground;
        if (geometry is null || brush is null)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        var side = Math.Min(width, height);
        if (side <= 0)
        {
            return;
        }

        // Scale the 24x24 box uniformly and centre it when the element is not square. A frozen transform per render
        // (renders only happen on resize/kind change): an unfrozen one kept per icon stays a live, change-tracked
        // render resource for each of the thousands of icons in tables and trees.
        var factor = side / SourceSize;
        var transform = new MatrixTransform(factor, 0, 0, factor, (width - side) / 2, (height - side) / 2);
        transform.Freeze();

        drawingContext.PushTransform(transform);
        drawingContext.DrawGeometry(brush, null, geometry);
        drawingContext.Pop();
    }

    private static bool IsValidSize(object value) =>
        value is double d && !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0;

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DmxIcon)d).ResolveKind();

    private static bool IsValidMaxSize(object value) => value is double d && !double.IsNaN(d) && d >= 0;

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // MaxSize is attached and inherited, so this also runs for the hosts that set it; only icons resize.
        if (d is not DmxIcon icon)
        {
            return;
        }

        var size = Math.Min(icon.Size, GetMaxSize(icon));
        icon.Width = size;
        icon.Height = size;
    }

    private void ResolveKind()
    {
        var kind = Kind;
        var geometry = string.IsNullOrWhiteSpace(kind)
            ? null
            : TryFindResource(ResourceKeyPrefix + kind) as Geometry;

        if (!ReferenceEquals(geometry, _resolvedGeometry))
        {
            _resolvedGeometry = geometry;
            InvalidateVisual();
        }
    }
}
