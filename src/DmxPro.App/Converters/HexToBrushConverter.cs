using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DmxPro.App.Converters;

/// <summary>
/// "#RRGGBB" → frozen <see cref="SolidColorBrush"/>. Returns null for null or invalid text, so a template trigger
/// on a null result can fall back to a theme brush (e.g. the accent).
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    /// <summary>Upper bound of cached brushes (colour pickers can produce many; channel types only a handful).</summary>
    private const int MaxCached = 256;

    // Frozen brushes by RGB: every fader of a big desk binds the same few channel-type colours (twice per fader), so
    // sharing them saves thousands of identical brush objects. Converters run on the UI thread only.
    private readonly Dictionary<int, SolidColorBrush> _cache = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text)
        {
            return null;
        }

        var span = text.AsSpan().Trim();
        if (span.Length != 7 || span[0] != '#'
            || !int.TryParse(span[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return null;
        }

        if (_cache.TryGetValue(rgb, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        if (_cache.Count >= MaxCached)
        {
            _cache.Clear();
        }

        _cache[rgb] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
