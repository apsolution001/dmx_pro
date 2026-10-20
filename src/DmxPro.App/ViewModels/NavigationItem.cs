namespace DmxPro.App.ViewModels;

/// <summary>An entry in the left navigation rail.</summary>
/// <param name="Title">Label shown under the icon.</param>
/// <param name="IconKind">Icon name from the app icon set (<c>Themes/Icons.xaml</c>, key <c>Icon.&lt;IconKind&gt;</c>).</param>
/// <param name="Shortcut">Keyboard shortcut shown in the tooltip, e.g. "Ctrl+1".</param>
/// <param name="Page">The page shown when the entry is selected.</param>
public sealed record NavigationItem(string Title, string IconKind, string Shortcut, PageViewModel Page)
{
    /// <summary>Tooltip text: what the section is for, plus its shortcut.</summary>
    public string ToolTip => $"{Page.Subtitle} ({Shortcut})";
}
