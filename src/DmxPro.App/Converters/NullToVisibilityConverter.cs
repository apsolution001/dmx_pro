using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DmxPro.App.Converters;

/// <summary>
/// Visible when the value is not null (or a non-empty string). Pass "Invert" to flip, or "Bool" to get a bool instead.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is string text ? text.Length > 0 : value is not null;
        if (parameter is "Invert")
        {
            hasValue = !hasValue;
        }

        if (parameter is "Bool")
        {
            return hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
