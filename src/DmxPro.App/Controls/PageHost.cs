using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DmxPro.App.Controls;

/// <summary>
/// Shows the current page and keeps every page view it has created alive (collapsed while another page is shown).
/// A plain <see cref="ContentControl"/> rebuilds the whole view (DataGrid, address map, fader desk, …) from its
/// DataTemplate on every navigation, which made switching pages visibly slow; here each view is built once.
/// </summary>
/// <remarks>
/// Views are resolved through the implicit page DataTemplates (one <see cref="ContentPresenter"/> per page ViewModel
/// instance). Hidden views are collapsed, so they take no layout or render time; they keep their view state (scroll
/// offsets, splitter positions). Page ViewModels still get <c>OnNavigatedFrom</c>/<c>OnNavigatedTo</c> from the shell,
/// and a hidden view gets <see cref="UIElement.IsVisibleChanged"/> (not <c>Unloaded</c>) when it is navigated away.
/// <para>
/// Transition: the old page is collapsed at once (no fade-out, so switching feels instant and the Virtual Console still
/// gets IsVisibleChanged immediately to release flashes - safety rule 5); the new page fades in while sliding up
/// <see cref="DmxMotion.PageEnterOffset"/> px over <see cref="DmxMotion.Normal"/> (ease-out). Only Opacity and a
/// RenderTransform are animated (no layout pass), and nothing plays when Windows animations are off.
/// </para>
/// </remarks>
public sealed class PageHost : Grid
{
    /// <summary>Identifies the <see cref="Page"/> dependency property.</summary>
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page),
        typeof(object),
        typeof(PageHost),
        new FrameworkPropertyMetadata(null, OnPageChanged));

    private static readonly DoubleAnimation FadeIn = Freeze(new DoubleAnimation(0, 1, DmxMotion.Normal));
    private static readonly DoubleAnimation SlideUp = Freeze(new DoubleAnimation(DmxMotion.PageEnterOffset, 0, DmxMotion.Normal));

    private readonly Dictionary<object, ContentPresenter> _views = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets or sets the page ViewModel to show.</summary>
    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PageHost)d).Show(e.NewValue);

    private void Show(object? page)
    {
        // Hide the old page first, so focus inside it is dropped before the new page takes over.
        foreach (var (key, view) in _views)
        {
            if (!ReferenceEquals(key, page))
            {
                view.Visibility = Visibility.Collapsed;
            }
        }

        if (page is null)
        {
            return;
        }

        if (!_views.TryGetValue(page, out var current))
        {
            current = new ContentPresenter { Content = page, Focusable = false };
            _views.Add(page, current);
            Children.Add(current);
        }

        current.Visibility = Visibility.Visible;

        // The first page (shown while the window is still loading) appears without a transition.
        if (IsLoaded)
        {
            PlayEnter(current);
        }
    }

    private static void PlayEnter(ContentPresenter view)
    {
        if (!DmxMotion.IsEnabled || FadeIn.Duration == TimeSpan.Zero)
        {
            return;
        }

        if (view.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            view.RenderTransform = shift;
        }

        // SnapshotAndReplace restarts cleanly when the user switches pages faster than the transition.
        view.BeginAnimation(OpacityProperty, FadeIn, HandoffBehavior.SnapshotAndReplace);
        shift.BeginAnimation(TranslateTransform.YProperty, SlideUp, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation Freeze(DoubleAnimation animation)
    {
        // FillBehavior.Stop: once finished the view is back on its base values (Opacity 1, no offset), no clock kept.
        animation.EasingFunction = DmxMotion.EaseOut;
        animation.FillBehavior = FillBehavior.Stop;
        animation.Freeze();
        return animation;
    }
}
