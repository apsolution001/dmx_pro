namespace DmxPro.Core.History;

/// <summary>
/// Names one item of a project the way <see cref="ProjectSnapshot"/> keys it: the profile id for profiles, the item's
/// <see cref="Guid"/> ("D" format) for everything else. A <see cref="ProjectItemKind.ConsoleWidget"/> also names its page.
/// </summary>
/// <param name="Kind">The list the item lives in.</param>
/// <param name="Id">The item key.</param>
/// <param name="PageId">For a console widget: the key of its page; otherwise null.</param>
public readonly record struct ProjectItemKey(ProjectItemKind Kind, string Id, string? PageId = null)
{
    /// <summary>Key of a profile.</summary>
    public static ProjectItemKey Profile(string profileId) => new(ProjectItemKind.Profile, profileId);

    /// <summary>Key of a fixture.</summary>
    public static ProjectItemKey Fixture(Guid id) => new(ProjectItemKind.Fixture, id.ToString());

    /// <summary>Key of a group.</summary>
    public static ProjectItemKey Group(Guid id) => new(ProjectItemKind.Group, id.ToString());

    /// <summary>Key of a function.</summary>
    public static ProjectItemKey Function(Guid id) => new(ProjectItemKind.Function, id.ToString());

    /// <summary>Key of a show.</summary>
    public static ProjectItemKey Show(Guid id) => new(ProjectItemKind.Show, id.ToString());

    /// <summary>Key of a console page.</summary>
    public static ProjectItemKey ConsolePage(Guid id) => new(ProjectItemKind.ConsolePage, id.ToString());

    /// <summary>Key of a widget on a console page.</summary>
    public static ProjectItemKey ConsoleWidget(Guid pageId, Guid widgetId) =>
        new(ProjectItemKind.ConsoleWidget, widgetId.ToString(), pageId.ToString());
}
