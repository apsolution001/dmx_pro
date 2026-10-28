namespace DmxPro.Core.History;

/// <summary>Which list of a project an item lives in (the sections of a <see cref="ProjectSnapshot"/>).</summary>
public enum ProjectItemKind
{
    /// <summary>A fixture profile (key: its profile id).</summary>
    Profile = 0,

    /// <summary>A patched fixture: its profile, mode and DMX address.</summary>
    Fixture = 1,

    /// <summary>A fixture group.</summary>
    Group = 2,

    /// <summary>A function (library, chaser-owned step or show copy).</summary>
    Function = 3,

    /// <summary>A show.</summary>
    Show = 4,

    /// <summary>A whole Virtual Console page.</summary>
    ConsolePage = 5,

    /// <summary>One widget of a Virtual Console page (<see cref="ProjectItemKey.PageId"/> names the page).</summary>
    ConsoleWidget = 6,
}
