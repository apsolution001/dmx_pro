using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DmxPro.App.Converters;

/// <summary>
/// Visible when the bound value's text equals the parameter (e.g. an enum value name). Several names can be given
/// separated by '|' ("ArtNet|Sacn"). Prefix the parameter with '!' to invert.
/// </summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = parameter as string ?? string.Empty;
        var invert = text.StartsWith('!');
        if (invert)
        {
            text = text[1..];
        }

        var current = value?.ToString() ?? string.Empty;
        var match = text.Split('|').Any(t => string.Equals(t, current, StringComparison.Ordinal));
        return match != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
