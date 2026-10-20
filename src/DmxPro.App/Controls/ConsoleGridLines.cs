using System.Windows;
using System.Windows.Media;

namespace DmxPro.App.Controls;

/// <summary>Draws the console page grid (one line per cell edge) in edit mode.</summary>
public sealed class ConsoleGridLines : FrameworkElement
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(ConsoleGridLines),
        new FrameworkPropertyMetadata(24, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(ConsoleGridLines),
        new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CellSizeProperty = DependencyProperty.Register(
        nameof(CellSize), typeof(double), typeof(ConsoleGridLines),
        new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(ConsoleGridLines),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double CellSize
    {
        get => (double)GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (LineBrush is null || CellSize <= 0)
        {
            return;
        }

        var pen = new Pen(LineBrush, 1);
        pen.Freeze();
        var width = Columns * CellSize;
        var height = Rows * CellSize;

        // +0.5 puts each 1 px line on a device pixel instead of blurring it over two.
        for (var c = 0; c <= Columns; c++)
        {
            var x = Math.Floor(c * CellSize) + 0.5;
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        for (var r = 0; r <= Rows; r++)
        {
            var y = Math.Floor(r * CellSize) + 0.5;
            drawingContext.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }
}
