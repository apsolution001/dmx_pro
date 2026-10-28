using System.Text.Json;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.History;

/// <summary>
/// Applies an outside change of the project file while live without touching what is on the lights (owner decision
/// 2026-10-28). Works on <see cref="ProjectSnapshot"/>s, item by item (the snapshot's per-item JSON), so the result is
/// restored with the normal undo path and every item that keeps its in-memory version stays the same object.
/// <list type="number">
/// <item><see cref="PlanReload"/>: the file's version of everything, except what <see cref="ProtectedContent"/> keeps:
/// frozen items keep their in-memory version (also when the file deleted them), frozen console widgets keep theirs inside
/// the file's version of their page, and what a page kept whole refers to stays when the file deleted it. What differs
/// from the file is returned as <see cref="ExternalMergeResult.Deferred"/>.</item>
/// <item><see cref="PlanRelease"/>: later, when something stopped, the waiting items that are no longer protected take
/// their file version; everything else (including edits made in the app meanwhile) stays as it is.</item>
/// </list>
/// Not thread-safe; runs on the thread that edits the project.
/// </summary>
public static class ExternalChangeMerge
{
    /// <summary>
    /// The content to restore when the file now holds <paramref name="disk"/> and the project holds
    /// <paramref name="current"/> (captured right before), keeping <paramref name="protection"/>.
    /// </summary>
    public static ExternalMergeResult PlanReload(ProjectSnapshot current, ProjectSnapshot disk, ProtectedContent protection)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(protection);
        if (protection.IsEmpty)
        {
            return new ExternalMergeResult(disk, [], 0);
        }

        var deferred = new List<ProjectItemKey>();
        var keepAlive = new HashSet<ProjectItemKey>();
        var pages = MergeSection(
            current.ConsolePages,
            disk.ConsolePages,
            diskOrder: true,
            (key, cur, dsk) => ChoosePage(key, cur, dsk, protection, deferred, keepAlive));

        SnapshotSection Merge(ProjectItemKind kind) => MergeSection(
            current.SectionOf(kind),
            disk.SectionOf(kind),
            diskOrder: true,
            (key, cur, dsk) => ChooseItem(new ProjectItemKey(kind, key), cur, dsk, protection, keepAlive, deferred));

        // Pages first: a page kept whole decides what has to stay alive in the other lists.
        var functions = Merge(ProjectItemKind.Function);
        var shows = Merge(ProjectItemKind.Show);
        var groups = Merge(ProjectItemKind.Group);
        var fixtures = Merge(ProjectItemKind.Fixture);
        var profiles = Merge(ProjectItemKind.Profile);
        var target = ProjectSnapshot.Create(
            disk.SchemaVersion, disk.Id, disk.Name, profiles, fixtures, disk.Stage, groups, functions, shows, pages);
        return new ExternalMergeResult(target, deferred, 0);
    }

    /// <summary>
    /// The content to restore when some of the <paramref name="deferred"/> items (held back from <paramref name="disk"/>
    /// by an earlier plan whose target was <paramref name="applied"/>) may be released now that only
    /// <paramref name="protection"/> is protected. Items edited in the app since <paramref name="applied"/> keep the app's
    /// version (<see cref="ExternalMergeResult.Discarded"/>). The project's name, stage and every item that was not waiting
    /// stay as they are in <paramref name="current"/>.
    /// </summary>
    public static ExternalMergeResult PlanRelease(
        ProjectSnapshot current,
        ProjectSnapshot disk,
        ProjectSnapshot applied,
        IReadOnlyCollection<ProjectItemKey> deferred,
        ProtectedContent protection)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(deferred);
        ArgumentNullException.ThrowIfNull(protection);

        var discarded = 0;
        var considered = new HashSet<ProjectItemKey>();
        var widgetsByPage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in deferred)
        {
            if (!applied.ItemEquals(current, key))
            {
                discarded++;
            }
            else if (key.Kind == ProjectItemKind.ConsoleWidget && key.PageId is { } pageId)
            {
                if (!widgetsByPage.TryGetValue(pageId, out var widgets))
                {
                    widgets = [];
                    widgetsByPage[pageId] = widgets;
                }

                widgets.Add(key.Id);
            }
            else
            {
                considered.Add(key);
            }
        }

        var remaining = new List<ProjectItemKey>();
        var keepAlive = new HashSet<ProjectItemKey>();
        var pages = MergeSection(
            current.ConsolePages,
            disk.ConsolePages,
            diskOrder: false,
            (key, cur, dsk) =>
            {
                if (considered.Contains(new ProjectItemKey(ProjectItemKind.ConsolePage, key)))
                {
                    return ChoosePage(key, cur, dsk, protection, remaining, keepAlive);
                }

                if (cur is null || !widgetsByPage.TryGetValue(key, out var widgets))
                {
                    return cur;
                }

                var released = new List<string>();
                foreach (var widget in widgets)
                {
                    if (protection.IsWidgetFrozen(key, widget))
                    {
                        remaining.Add(new ProjectItemKey(ProjectItemKind.ConsoleWidget, widget, key));
                    }
                    else
                    {
                        released.Add(widget);
                    }
                }

                return released.Count == 0 ? cur : MergeWidgets(cur, dsk, released);
            });

        SnapshotSection Merge(ProjectItemKind kind) => MergeSection(
            current.SectionOf(kind),
            disk.SectionOf(kind),
            diskOrder: false,
            (key, cur, dsk) =>
            {
                var item = new ProjectItemKey(kind, key);
                return considered.Contains(item) ? ChooseItem(item, cur, dsk, protection, keepAlive, remaining) : cur;
            });

        var functions = Merge(ProjectItemKind.Function);
        var shows = Merge(ProjectItemKind.Show);
        var groups = Merge(ProjectItemKind.Group);
        var fixtures = Merge(ProjectItemKind.Fixture);
        var profiles = Merge(ProjectItemKind.Profile);
        var target = ProjectSnapshot.Create(
            current.SchemaVersion, current.Id, current.Name, profiles, fixtures, current.Stage, groups, functions, shows, pages);
        return new ExternalMergeResult(target, remaining, discarded);
    }

    /// <summary>
    /// Cheap check (no project capture) whether <see cref="PlanRelease"/> would release anything of
    /// <paramref name="deferred"/> under <paramref name="protection"/>.
    /// </summary>
    public static bool HasReleasable(IReadOnlyCollection<ProjectItemKey> deferred, ProjectSnapshot disk, ProtectedContent protection)
    {
        ArgumentNullException.ThrowIfNull(deferred);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(protection);

        // What still-held whole pages keep alive.
        var keepAlive = new HashSet<ProjectItemKey>();
        foreach (var key in deferred)
        {
            if (key.Kind == ProjectItemKind.ConsolePage && IsPageHeldWhole(key.Id, disk, protection) &&
                protection.PageDependencies.TryGetValue(key.Id, out var dependencies))
            {
                keepAlive.UnionWith(dependencies);
            }
        }

        foreach (var key in deferred)
        {
            var held = key.Kind switch
            {
                ProjectItemKind.ConsoleWidget => protection.IsWidgetFrozen(key.PageId ?? string.Empty, key.Id),
                ProjectItemKind.ConsolePage => IsPageHeldWhole(key.Id, disk, protection),
                _ => protection.Frozen.Contains(key) || (keepAlive.Contains(key) && !disk.Contains(key)),
            };

            if (!held)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The item's JSON in <paramref name="snapshot"/>, or null when it does not hold it.</summary>
    internal static byte[]? ItemBytes(ProjectSnapshot snapshot, ProjectItemKey key)
    {
        if (key.Kind != ProjectItemKind.ConsoleWidget)
        {
            return Find(snapshot.SectionOf(key.Kind), key.Id);
        }

        return key.PageId is { } pageId && Find(snapshot.ConsolePages, pageId) is { } page ? WidgetBytes(page, key.Id) : null;
    }

    /// <summary>A page that keeps its whole in-memory version: the console is frozen, or the file deleted a page with frozen widgets.</summary>
    private static bool IsPageHeldWhole(string pageId, ProjectSnapshot disk, ProtectedContent protection) =>
        protection.FreezeConsole ||
        (protection.FrozenWidgets.ContainsKey(pageId) && Find(disk.ConsolePages, pageId) is null);

    /// <summary>One profile / fixture / group / function / show: keep the in-memory version or take the file's.</summary>
    private static byte[]? ChooseItem(
        ProjectItemKey key,
        byte[]? cur,
        byte[]? dsk,
        ProtectedContent protection,
        HashSet<ProjectItemKey> keepAlive,
        List<ProjectItemKey> deferred)
    {
        var keep = cur is not null && (protection.Frozen.Contains(key) || (dsk is null && keepAlive.Contains(key)));
        var chosen = keep ? cur : dsk;
        if (!Same(chosen, dsk))
        {
            deferred.Add(key);
        }

        return chosen;
    }

    /// <summary>One console page: kept whole, the file's page with its frozen widgets kept, or the file's page.</summary>
    private static byte[]? ChoosePage(
        string pageId,
        byte[]? cur,
        byte[]? dsk,
        ProtectedContent protection,
        List<ProjectItemKey> deferred,
        HashSet<ProjectItemKey> keepAlive)
    {
        if (protection.FreezeConsole || (dsk is null && cur is not null && protection.FrozenWidgets.ContainsKey(pageId)))
        {
            // A held flash (any console rebuild would end it), or a page with playing widgets that the file deleted.
            if (!Same(cur, dsk))
            {
                deferred.Add(new ProjectItemKey(ProjectItemKind.ConsolePage, pageId));
                if (cur is not null && protection.PageDependencies.TryGetValue(pageId, out var dependencies))
                {
                    keepAlive.UnionWith(dependencies);
                }
            }

            return cur;
        }

        if (cur is null || dsk is null || !protection.FrozenWidgets.TryGetValue(pageId, out var frozen))
        {
            return dsk;
        }

        var keptWidgets = new List<string>();
        foreach (var widget in frozen)
        {
            var mine = WidgetBytes(cur, widget);
            if (mine is null)
            {
                continue;
            }

            keptWidgets.Add(widget);
            if (!Same(mine, WidgetBytes(dsk, widget)))
            {
                deferred.Add(new ProjectItemKey(ProjectItemKind.ConsoleWidget, widget, pageId));
            }
        }

        return keptWidgets.Count == 0 ? dsk : MergeWidgets(dsk, cur, keptWidgets);
    }

    /// <summary>
    /// <paramref name="basePage"/> with the widgets <paramref name="widgetIds"/> taken from <paramref name="otherPage"/>:
    /// replaced in place, added when the base lacks them, removed when the other page lacks them.
    /// </summary>
    private static byte[] MergeWidgets(byte[] basePage, byte[]? otherPage, IEnumerable<string> widgetIds)
    {
        var page = ReadPage(basePage);
        var other = otherPage is null ? null : ReadPage(otherPage);
        foreach (var widgetId in widgetIds)
        {
            if (!Guid.TryParse(widgetId, out var id))
            {
                continue;
            }

            var at = page.Widgets.FindIndex(w => w is not null && w.Id == id);
            var replacement = other?.Widgets.Find(w => w is not null && w.Id == id);
            if (replacement is not null)
            {
                if (at >= 0)
                {
                    page.Widgets[at] = replacement;
                }
                else
                {
                    page.Widgets.Add(replacement);
                }
            }
            else if (at >= 0)
            {
                page.Widgets.RemoveAt(at);
            }
        }

        var merged = JsonSerializer.SerializeToUtf8Bytes(page, ProjectSnapshot.Options);
        return SnapshotSection.SameBytes(merged, basePage) ? basePage : merged;
    }

    private static ConsolePage ReadPage(byte[] json) =>
        JsonSerializer.Deserialize<ConsolePage>(json, ProjectSnapshot.Options)
        ?? throw new InvalidOperationException("A console page of the project file could not be read.");

    /// <summary>The JSON of one widget of a page, or null when the page does not hold it.</summary>
    private static byte[]? WidgetBytes(byte[] page, string widgetId)
    {
        if (!Guid.TryParse(widgetId, out var id))
        {
            return null;
        }

        foreach (var widget in ReadPage(page).Widgets)
        {
            if (widget is not null && widget.Id == id)
            {
                // As the base type, so the "type" discriminator is part of the comparison.
                return JsonSerializer.SerializeToUtf8Bytes<ConsoleWidget>(widget, ProjectSnapshot.Options);
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a section from both versions: <paramref name="choose"/> gets each key with its in-memory and file JSON
    /// (null when absent) and returns the JSON to keep (null: leave the item out). The order follows the file
    /// (<paramref name="diskOrder"/>) or the project; items only in the other list go to their own index.
    /// </summary>
    private static SnapshotSection MergeSection(
        SnapshotSection current,
        SnapshotSection disk,
        bool diskOrder,
        Func<string, byte[]?, byte[]?, byte[]?> choose)
    {
        var currentIndex = IndexOf(current);
        var diskIndex = IndexOf(disk);
        var primary = diskOrder ? disk : current;
        var secondary = diskOrder ? current : disk;
        var primaryIndex = diskOrder ? diskIndex : currentIndex;
        var keys = new List<string>(primary.Count);
        var names = new List<string>(primary.Count);
        var items = new List<byte[]>(primary.Count);
        var done = new HashSet<string>(StringComparer.Ordinal);

        void Add(string key, int insertAt)
        {
            if (!done.Add(key))
            {
                return;
            }

            var hasCurrent = currentIndex.TryGetValue(key, out var ci);
            var hasDisk = diskIndex.TryGetValue(key, out var di);
            var cur = hasCurrent ? current.Items[ci] : null;
            var dsk = hasDisk ? disk.Items[di] : null;
            var chosen = choose(key, cur, dsk);
            if (chosen is null)
            {
                return;
            }

            var name = hasDisk && (ReferenceEquals(chosen, dsk) || !hasCurrent) ? disk.Names[di] : current.Names[ci];
            var at = insertAt < 0 ? keys.Count : Math.Min(insertAt, keys.Count);
            keys.Insert(at, key);
            names.Insert(at, name);
            items.Insert(at, chosen);
        }

        for (var i = 0; i < primary.Count; i++)
        {
            Add(primary.Keys[i], -1);
        }

        for (var i = 0; i < secondary.Count; i++)
        {
            if (!primaryIndex.ContainsKey(secondary.Keys[i]))
            {
                Add(secondary.Keys[i], i);
            }
        }

        var result = new SnapshotSection([.. keys], [.. names], [.. items]);
        if (result.ContentEquals(current))
        {
            return current;
        }

        return result.ContentEquals(disk) ? disk : result;
    }

    private static Dictionary<string, int> IndexOf(SnapshotSection section)
    {
        var index = new Dictionary<string, int>(section.Count, StringComparer.Ordinal);
        for (var i = 0; i < section.Count; i++)
        {
            index.TryAdd(section.Keys[i], i);
        }

        return index;
    }

    private static byte[]? Find(SnapshotSection section, string key)
    {
        var at = section.IndexOfKey(key);
        return at < 0 ? null : section.Items[at];
    }

    private static bool Same(byte[]? a, byte[]? b) =>
        a is null ? b is null : b is not null && SnapshotSection.SameBytes(a, b);
}
