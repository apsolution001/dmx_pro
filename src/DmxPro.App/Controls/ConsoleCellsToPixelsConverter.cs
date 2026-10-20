using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DmxPro.App.Controls;

/// <summary>Multi-binding converter: [cells, cell size in px] → pixels (console widget position and size).</summary>
public sealed class ConsoleCellsToPixelsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not double cellSize)
        {
            return DependencyProperty.UnsetValue;
        }

        var cells = values[0] switch
        {
            int i => i,
            double d => d,
            _ => 0.0,
        };
        return cells * cellSize;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
