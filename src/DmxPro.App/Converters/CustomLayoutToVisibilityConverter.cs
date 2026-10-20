using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DmxPro.Core.Profiles;

namespace DmxPro.App.Converters;

/// <summary>
/// <see cref="FixtureProfile"/> → Visible for custom layouts made in the channel layout editor
/// (<see cref="ChannelLayout.IsCustom"/>), Collapsed otherwise. Drives the "Custom" badge in profile lists.
/// </summary>
public sealed class CustomLayoutToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FixtureProfile profile && ChannelLayout.IsCustom(profile) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
