using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.History;

/// <summary>
/// The part of a project that is on the lights right now and therefore must not be changed by an outside change of the
/// project file while live (owner decision 2026-10-28; <see cref="ExternalChangeMerge"/>). Built from what plays with
/// <see cref="FromPlaying"/>:
/// <list type="bullet">
/// <item><b>Frozen</b> (keeps its in-memory version): every playing function with everything it plays (chaser steps,
/// owned step functions, recursively), every playing show with its items' functions and its show-owned copies, the
/// fixtures those functions drive (their patch) and the fixtures' profiles.</item>
/// <item><b>Frozen widgets</b>: Virtual Console function / show widgets whose function or show plays; the rest of their
/// page still takes the file's version. While a flash button is held, the whole console is frozen (a console rebuild would
/// end the held flash).</item>
/// <item><b>Page dependencies</b>: what a page that has to be kept whole refers to (its widgets' functions, shows and
/// groups, with their own dependencies), kept only when the file deleted them.</item>
/// </list>
/// Groups never change the output (group levels are not in the engine yet), so they are only kept as page dependencies.
/// The stage layout is view state and never frozen.
/// </summary>
public sealed class ProtectedContent
{
    /// <summary>Nothing is protected (not live, or nothing plays): the file is applied as a whole.</summary>
    public static readonly ProtectedContent None = new(
        new HashSet<ProjectItemKey>(),
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
        new Dictionary<string, HashSet<ProjectItemKey>>(StringComparer.Ordinal),
        freezeConsole: false);

    private ProtectedContent(
        HashSet<ProjectItemKey> frozen,
        Dictionary<string, HashSet<string>> frozenWidgets,
        Dictionary<string, HashSet<ProjectItemKey>> pageDependencies,
        bool freezeConsole)
    {
        Frozen = frozen;
        FrozenWidgets = frozenWidgets;
        PageDependencies = pageDependencies;
        FreezeConsole = freezeConsole;
    }

    /// <summary>True when nothing is protected.</summary>
    public bool IsEmpty => Frozen.Count == 0 && FrozenWidgets.Count == 0 && !FreezeConsole;

    /// <summary>True while a flash button is held: no console page may change (the console would be rebuilt).</summary>
    public bool FreezeConsole { get; }

    /// <summary>Profiles, fixtures, functions and shows that keep their in-memory version.</summary>
    public IReadOnlySet<ProjectItemKey> FrozenItems => Frozen;

    internal HashSet<ProjectItemKey> Frozen { get; }

    /// <summary>Page key → keys of its widgets that keep their in-memory version.</summary>
    internal Dictionary<string, HashSet<string>> FrozenWidgets { get; }

    /// <summary>Page key → everything it refers to (closure), for pages that may have to be kept whole.</summary>
    internal Dictionary<string, HashSet<ProjectItemKey>> PageDependencies { get; }

    /// <summary>True when the widget <paramref name="widgetId"/> of page <paramref name="pageId"/> keeps its version.</summary>
    public bool IsWidgetFrozen(string pageId, string widgetId) =>
        FreezeConsole || (FrozenWidgets.TryGetValue(pageId, out var widgets) && widgets.Contains(widgetId));

    /// <summary>
    /// What must not change in <paramref name="project"/> while <paramref name="playingFunctions"/> run and
    /// <paramref name="playingShows"/> play. <paramref name="flashHeld"/>: a flash button is held down right now.
    /// Runs on the thread that edits the project (the UI thread).
    /// </summary>
    public static ProtectedContent FromPlaying(
        Project project,
        IReadOnlyCollection<Guid> playingFunctions,
        IReadOnlyCollection<Guid> playingShows,
        bool flashHeld)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(playingFunctions);
        ArgumentNullException.ThrowIfNull(playingShows);
        if (playingFunctions.Count == 0 && playingShows.Count == 0 && !flashHeld)
        {
            return None;
        }

        var frozen = new HashSet<ProjectItemKey>();
        AddClosure(project, playingFunctions, playingShows, [], frozen);

        var frozenWidgets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var pageDependencies = new Dictionary<string, HashSet<ProjectItemKey>>(StringComparer.Ordinal);
        var functionSet = playingFunctions as IReadOnlySet<Guid> ?? playingFunctions.ToHashSet();
        var showSet = playingShows as IReadOnlySet<Guid> ?? playingShows.ToHashSet();
        foreach (var page in project.ConsolePages)
        {
            var pageKey = page.Id.ToString();
            HashSet<string>? widgets = null;
            foreach (var widget in page.Widgets)
            {
                var active = widget switch
                {
                    FunctionWidget function => functionSet.Contains(function.FunctionId),
                    ShowWidget show => showSet.Contains(show.ShowId),
                    _ => false,
                };

                if (active)
                {
                    widgets ??= new HashSet<string>(StringComparer.Ordinal);
                    widgets.Add(widget.Id.ToString());
                }
            }

            if (widgets is not null)
            {
                frozenWidgets[pageKey] = widgets;
            }

            if (widgets is not null || flashHeld)
            {
                pageDependencies[pageKey] = PageClosure(project, page);
            }
        }

        return new ProtectedContent(frozen, frozenWidgets, pageDependencies, flashHeld);
    }

    /// <summary>Everything <paramref name="page"/>'s widgets refer to, with their own dependencies.</summary>
    private static HashSet<ProjectItemKey> PageClosure(Project project, ConsolePage page)
    {
        var functions = new List<Guid>();
        var shows = new List<Guid>();
        var groups = new List<Guid>();
        foreach (var widget in page.Widgets)
        {
            switch (widget)
            {
                case FunctionWidget function:
                    functions.Add(function.FunctionId);
                    break;
                case ShowWidget show:
                    shows.Add(show.ShowId);
                    break;
                case SpeedWidget { FunctionId: { } speedTarget }:
                    functions.Add(speedTarget);
                    break;
                case GroupLevelWidget group:
                    groups.Add(group.GroupId);
                    break;
            }
        }

        var closure = new HashSet<ProjectItemKey>();
        AddClosure(project, functions, shows, groups, closure);
        return closure;
    }

    /// <summary>
    /// Adds the functions, shows and groups with everything they play or drive: referenced and owned functions
    /// (recursively), show items and show-owned copies, fixtures, and the fixtures' profiles.
    /// </summary>
    private static void AddClosure(
        Project project,
        IEnumerable<Guid> functionIds,
        IEnumerable<Guid> showIds,
        IEnumerable<Guid> groupIds,
        HashSet<ProjectItemKey> into)
    {
        var pending = new Stack<Guid>(functionIds);
        var fixtures = new HashSet<Guid>();
        foreach (var showId in showIds)
        {
            if (!into.Add(ProjectItemKey.Show(showId)) || project.FindShow(showId) is not { } show)
            {
                continue;
            }

            foreach (var item in show.AllItems)
            {
                pending.Push(item.FunctionId);
            }

            foreach (var function in project.Functions)
            {
                if (function.OwnerShowId == showId)
                {
                    pending.Push(function.Id);
                }
            }
        }

        foreach (var groupId in groupIds)
        {
            if (into.Add(ProjectItemKey.Group(groupId)) && project.FindGroup(groupId) is { } group)
            {
                fixtures.UnionWith(group.FixtureIds);
            }
        }

        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!into.Add(ProjectItemKey.Function(id)) || project.FindFunction(id) is not { } function)
            {
                continue;
            }

            foreach (var referenced in function.ReferencedFunctionIds())
            {
                pending.Push(referenced);
            }

            foreach (var owned in project.OwnedFunctions(id))
            {
                pending.Push(owned.Id);
            }

            fixtures.UnionWith(function.ReferencedFixtureIds());
        }

        foreach (var fixtureId in fixtures)
        {
            into.Add(ProjectItemKey.Fixture(fixtureId));
            if (project.FindFixture(fixtureId) is { } fixture)
            {
                // The snapshot keys profiles by their own id; fixtures may spell it in another case.
                into.Add(ProjectItemKey.Profile(project.FindProfile(fixture.ProfileId)?.Id ?? fixture.ProfileId));
            }
        }
    }
}
