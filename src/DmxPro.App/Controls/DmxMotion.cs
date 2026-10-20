using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DmxPro.App.Controls;

/// <summary>
/// Motion tokens of the design system, used from XAML as <c>Duration="{x:Static c:DmxMotion.Fast}"</c> and
/// <c>EasingFunction="{x:Static c:DmxMotion.EaseOut}"</c>. Motion is short, ease-out, never bouncy, and only on
/// Opacity / RenderTransform (no layout animation). When Windows has client-area animations turned off ("Animation
/// effects" / reduced motion) or WPF renders in software, every duration is zero, so states change instantly.
/// </summary>
/// <remarks>
/// Never animate anything bound to live DMX values or per-item in large lists (fader desk, address map).
/// </remarks>
public static class DmxMotion
{
    /// <summary>Hover and press colour fades (about 100 ms), or zero when animations are off.</summary>
    public static Duration Fast { get; } = Create(TimeSpan.FromMilliseconds(100));

    /// <summary>Things appearing: page enter, message bars, selection indicators (150 ms), or zero when off.</summary>
    public static Duration Normal { get; } = Create(TimeSpan.FromMilliseconds(150));

    /// <summary>Standard deceleration curve for everything that appears (cubic ease-out, frozen).</summary>
    public static IEasingFunction EaseOut { get; } = CreateEaseOut();

    /// <summary>How far (px) a page starts below its final position before sliding up into place.</summary>
    public static double PageEnterOffset => 8;

    /// <summary>
    /// Whether animations should play right now. Read live (not cached) by code-behind animations such as
    /// <see cref="PageHost"/>, so turning off Windows animations takes effect without a restart there.
    /// </summary>
    public static bool IsEnabled =>
        SystemParameters.ClientAreaAnimation && (RenderCapability.Tier >> 16) > 0;

    private static Duration Create(TimeSpan time) => new(IsEnabled ? time : TimeSpan.Zero);

    private static CubicEase CreateEaseOut()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
