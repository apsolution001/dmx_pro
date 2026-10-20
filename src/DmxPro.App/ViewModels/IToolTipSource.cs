namespace DmxPro.App.ViewModels;

/// <summary>
/// An item whose tooltip text is built on demand (see <c>Controls/LazyToolTip</c>) instead of being bound, so items
/// that change many times a second (Test page faders, DMX monitor cells) do not rebuild tooltip strings on every change.
/// </summary>
public interface IToolTipSource
{
    /// <summary>The tooltip text right now, or null/empty for no tooltip.</summary>
    string? ToolTip { get; }
}
