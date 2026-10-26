namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One inline message under the fade editor's scene panels. <see cref="IsWarning"/> = the fade will not do what the
/// user likely wants (warning icon); otherwise a plain explanation of what will happen (info icon).
/// </summary>
public sealed record FadeNotice(string Text, bool IsWarning)
{
    public string IconKind => IsWarning ? "Warning" : "Info";

    public string AutomationName => (IsWarning ? "Warning: " : "Note: ") + Text;
}
