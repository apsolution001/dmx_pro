using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DmxPro.App.Controls;

/// <summary>
/// Turns a widget colour "#RRGGBB" into a brush. With ConverterParameter "Contrast" it returns black or white,
/// whichever reads better on that colour. Returns <see cref="DependencyProperty.UnsetValue"/> for no/invalid colour,
/// so a style's theme brush stays in effect.
/// </summary>
public sealed class ConsoleColorBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length != 7 || text[0] != '#'
            || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return DependencyProperty.UnsetValue;
        }

        var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (parameter is "Contrast")
        {
            // Relative luminance (sRGB weights): bright colours (yellow, white, cyan, green) get black text.
            var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;
            return luminance > 0.5 ? Brushes.Black : Brushes.White;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
