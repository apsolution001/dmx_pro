using System.Windows;
using System.Windows.Controls;
using DmxPro.App.ViewModels;

namespace DmxPro.App.Controls;

/// <summary>
/// Attached behaviour: the element's tooltip is read from its <see cref="FrameworkElement.DataContext"/>
/// (<see cref="IToolTipSource"/>) only when the tooltip is about to open. A <c>ToolTip="{Binding ToolTip}"</c> binding
/// re-runs the string-building getter on every change notification; on the Test page that was hundreds of faders and
/// 512 monitor cells at ~30 Hz. The text is current when the tooltip opens and stays as it was while it is open.
/// </summary>
/// <example>
/// <code>&lt;Border c:LazyToolTip.IsEnabled="True" /&gt;</code>
/// </example>
public static class LazyToolTip
{
    /// <summary>Identifies the <c>IsEnabled</c> attached property.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(LazyToolTip),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Gets whether the element reads its tooltip lazily.</summary>
    /// <param name="element">The element to read from.</param>
    /// <returns>True when the tooltip comes from <see cref="IToolTipSource"/> on opening.</returns>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>Sets whether the element reads its tooltip lazily.</summary>
    /// <param name="element">The element to set it on.</param>
    /// <param name="value">True to read the tooltip from <see cref="IToolTipSource"/> on opening.</param>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.ToolTipOpening -= OnToolTipOpening;
        if (e.NewValue is true)
        {
            // Placeholder: ToolTipOpening is only raised when the element has a tooltip; the handler replaces it.
            element.ToolTip = string.Empty;
            element.ToolTipOpening += OnToolTipOpening;
        }
        else
        {
            element.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var text = (element.DataContext as IToolTipSource)?.ToolTip;
        if (string.IsNullOrEmpty(text))
        {
            e.Handled = true; // nothing to say: no empty tooltip box
            return;
        }

        element.ToolTip = text;
    }
}
