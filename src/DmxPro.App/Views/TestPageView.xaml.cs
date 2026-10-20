using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DmxPro.App.Views;

/// <summary>
/// Test &amp; Preview page: the lights of one universe as a QLC+-style desk of live channel faders, the Grand Master and
/// the DMX monitor.
/// </summary>
public partial class TestPageView : UserControl
{
    public TestPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The desk only scrolls sideways and the faders ignore the wheel (no accidental live changes), so the wheel scrolls
    /// the desk horizontally. View-only behaviour.
    /// </summary>
    private void OnDeskMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer(FixtureList) is not { } scroller)
        {
            return;
        }

        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
