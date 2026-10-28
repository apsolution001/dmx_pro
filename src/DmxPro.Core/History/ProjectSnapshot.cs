using System.Buffers;
using System.Text.Json;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;
using DmxPro.Core.Stage;

namespace DmxPro.Core.History;

/// <summary>
/// An immutable copy of a project's content for undo/redo, stored as compact JSON per item (the same serializer as the
/// project file, so everything that is saved is undone). Items equal to the previous snapshot's share its byte arrays.
/// <see cref="RestoreInto"/> puts the content back into the live <see cref="Project"/> object in place, keeping every
/// item that did not change as the same object, so open editors, rows and the engine's running functions of untouched
/// items are not disturbed.
/// </summary>
public sealed class ProjectSnapshot
{
    // Same settings as the project file, without indentation (smaller, faster).
    internal static readonly JsonSerializerOptions Options = new(DmxJson.Options) { WriteIndented = false };

    private ProjectSnapshot(
        int schemaVersion,
        Guid id,
        string name,
        SnapshotSection profiles,
        SnapshotSection fixtures,
        byte[] stage,
        SnapshotSection groups,
        SnapshotSection functions,
        SnapshotSection shows,
        SnapshotSection consolePages)
    {
        SchemaVersion = schemaVersion;
        Id = id;
        Name = name;
        Profiles = profiles;
        Fixtures = fixtures;
        Stage = stage;
        Groups = groups;
        Functions = functions;
        Shows = shows;
        ConsolePages = consolePages;
    }

    /// <summary>Project name at the time of the snapshot.</summary>
    public string Name { get; }

    /// <summary>Bytes held by this snapshot's items (shared arrays counted in full; an upper bound of its own cost).</summary>
    public long TotalBytes
    {
        get
        {
            long total = Stage.Length;
            foreach (var section in Sections)
            {
                foreach (var item in section.Items)
                {
                    total += item.Length;
                }
            }

            return total;
        }
    }

    internal int SchemaVersion { get; }

    internal Guid Id { get; }

    internal SnapshotSection Profiles { get; }

    internal SnapshotSection Fixtures { get; }

    internal byte[] Stage { get; }

    internal SnapshotSection Groups { get; }

    internal SnapshotSection Functions { get; }

    internal SnapshotSection Shows { get; }

    internal SnapshotSection ConsolePages { get; }

    private IEnumerable<SnapshotSection> Sections => [Profiles, Fixtures, Groups, Functions, Shows, ConsolePages];

    /// <summary>
    /// Captures <paramref name="project"/>. Pass the previous snapshot so unchanged items share its memory. Runs on the
    /// thread that edits the project (the UI thread); never on the engine tick.
    /// </summary>
    public static ProjectSnapshot Capture(Project project, ProjectSnapshot? previous = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        using var writer = new ItemWriter();
        var stageJson = writer.Write(project.Stage);
        var stage = previous is not null && previous.Stage.AsSpan().SequenceEqual(stageJson) ? previous.Stage : stageJson.ToArray();

        return new ProjectSnapshot(
            project.SchemaVersion,
            project.Id,
            project.Name,
            CaptureSection(project.Profiles, previous?.Profiles, p => p.Id ?? string.Empty, p => p.DisplayName, writer),
            CaptureSection(project.Fixtures, previous?.Fixtures, f => f.Id.ToString(), f => f.Name, writer),
            stage,
            CaptureSection(project.Groups, previous?.Groups, g => g.Id.ToString(), g => g.Name, writer),
            CaptureSection(project.Functions, previous?.Functions, f => f.Id.ToString(), f => f.Name, writer),
            CaptureSection(project.Shows, previous?.Shows, s => s.Id.ToString(), s => s.Name, writer),
            CaptureSection(project.ConsolePages, previous?.ConsolePages, p => p.Id.ToString(), p => p.Name, writer));
    }

    /// <summary>A snapshot assembled from sections of others (<see cref="ExternalChangeMerge"/>).</summary>
    internal static ProjectSnapshot Create(
        int schemaVersion,
        Guid id,
        string name,
        SnapshotSection profiles,
        SnapshotSection fixtures,
        byte[] stage,
        SnapshotSection groups,
        SnapshotSection functions,
        SnapshotSection shows,
        SnapshotSection consolePages) =>
        new(schemaVersion, id, name, profiles, fixtures, stage, groups, functions, shows, consolePages);

    /// <summary>The section holding items of <paramref name="kind"/> (console widgets live in the pages).</summary>
    internal SnapshotSection SectionOf(ProjectItemKind kind) => kind switch
    {
        ProjectItemKind.Profile => Profiles,
        ProjectItemKind.Fixture => Fixtures,
        ProjectItemKind.Group => Groups,
        ProjectItemKind.Function => Functions,
        ProjectItemKind.Show => Shows,
        _ => ConsolePages,
    };

    /// <summary>True when this snapshot holds the item (for a console widget: its page holds it).</summary>
    public bool Contains(ProjectItemKey key) => ExternalChangeMerge.ItemBytes(this, key) is not null;

    /// <summary>True when both snapshots hold the item with the same content, or both do not hold it.</summary>
    public bool ItemEquals(ProjectSnapshot other, ProjectItemKey key)
    {
        ArgumentNullException.ThrowIfNull(other);
        var mine = ExternalChangeMerge.ItemBytes(this, key);
        var theirs = ExternalChangeMerge.ItemBytes(other, key);
        return mine is null ? theirs is null : theirs is not null && SnapshotSection.SameBytes(mine, theirs);
    }

    /// <summary>True when both snapshots hold exactly the same project content.</summary>
    public bool ContentEquals(ProjectSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return ReferenceEquals(this, other) ||
            (SchemaVersion == other.SchemaVersion &&
             Id == other.Id &&
             string.Equals(Name, other.Name, StringComparison.Ordinal) &&
             SnapshotSection.SameBytes(Stage, other.Stage) &&
             Profiles.ContentEquals(other.Profiles) &&
             Fixtures.ContentEquals(other.Fixtures) &&
             Groups.ContentEquals(other.Groups) &&
             Functions.ContentEquals(other.Functions) &&
             Shows.ContentEquals(other.Shows) &&
             ConsolePages.ContentEquals(other.ConsolePages));
    }

    /// <summary>A new, independent project with this content (for tests and tools; undo uses <see cref="RestoreInto"/>).</summary>
    public Project ToProject()
    {
        var project = new Project();
        RestoreInto(project, Capture(project));
        return project;
    }

    /// <summary>
    /// Makes <paramref name="project"/> hold this snapshot's content, in place: the <see cref="Project"/> object and its
    /// lists stay the same instances, items whose content is unchanged stay the same objects, changed or re-added items
    /// are read back from JSON. <paramref name="current"/> must have been captured from <paramref name="project"/> right
    /// before (its items line up with the project's lists).
    /// <para>
    /// One exception: an automatic stage placement (lights placed for viewing, layout not arranged by the owner) is kept
    /// when this snapshot has an empty, unarranged stage — the placement is view state, not an edit to undo.
    /// </para>
    /// </summary>
    public void RestoreInto(Project project, ProjectSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(current);

        // Read everything first, then apply: a snapshot that cannot be read leaves the project untouched.
        var profiles = PlanSection(project.Profiles, Profiles, current.Profiles);
        var fixtures = PlanSection(project.Fixtures, Fixtures, current.Fixtures);
        var stage = PlanStage(project, current);
        var groups = PlanSection(project.Groups, Groups, current.Groups);
        var functions = PlanSection(project.Functions, Functions, current.Functions);
        var shows = PlanSection(project.Shows, Shows, current.Shows, show => show.NormalizeAudio());
        var consolePages = PlanSection(project.ConsolePages, ConsolePages, current.ConsolePages);

        project.SchemaVersion = SchemaVersion;
        project.Id = Id;
        project.Name = Name;
        Apply(project.Profiles, profiles);
        Apply(project.Fixtures, fixtures);
        if (stage is not null)
        {
            project.Stage = stage;
        }

        Apply(project.Groups, groups);
        Apply(project.Functions, functions);
        Apply(project.Shows, shows);
        Apply(project.ConsolePages, consolePages);
    }

    /// <summary>Lists stay the same instances (pages and services may hold them).</summary>
    private static void Apply<T>(List<T> target, List<T>? items)
    {
        if (items is not null)
        {
            target.Clear();
            target.AddRange(items);
        }
    }

    private static SnapshotSection CaptureSection<T>(
        List<T> items,
        SnapshotSection? previous,
        Func<T, string> keyOf,
        Func<T, string> nameOf,
        ItemWriter writer)
        where T : class
    {
        var count = items.Count;
        if (count == 0)
        {
            return SnapshotSection.Empty;
        }

        var keys = new string[count];
        var names = new string[count];
        var bytes = new byte[count][];
        var allShared = previous is not null && previous.Count == count;
        Dictionary<string, int>? previousIndex = null;
        for (var i = 0; i < count; i++)
        {
            var item = items[i];

            // Serialized as T (LightFunction for functions), so polymorphic items carry their "type" discriminator.
            var json = writer.Write(item);
            keys[i] = item is null ? string.Empty : keyOf(item) ?? string.Empty;
            names[i] = item is null ? string.Empty : nameOf(item) ?? string.Empty;

            byte[]? reused = null;
            var shared = false;
            if (previous is not null)
            {
                var at = i < previous.Count && string.Equals(previous.Keys[i], keys[i], StringComparison.Ordinal)
                    ? i
                    : FindPrevious(previous, keys[i], ref previousIndex);
                if (at >= 0 && previous.Items[at].AsSpan().SequenceEqual(json))
                {
                    reused = previous.Items[at];
                    shared = at == i;
                }
            }

            allShared &= shared;

            // Only changed items allocate: unchanged ones point at the previous snapshot's bytes.
            bytes[i] = reused ?? json.ToArray();
        }

        // Nothing changed in this list: share the whole section (cheap comparisons later).
        if (allShared && previous is not null)
        {
            return previous;
        }

        return new SnapshotSection(keys, names, bytes);
    }

    private static int FindPrevious(SnapshotSection previous, string key, ref Dictionary<string, int>? index)
    {
        if (index is null)
        {
            index = new Dictionary<string, int>(previous.Count, StringComparer.Ordinal);
            for (var i = 0; i < previous.Count; i++)
            {
                index.TryAdd(previous.Keys[i], i);
            }
        }

        return index.TryGetValue(key, out var at) ? at : -1;
    }

    /// <summary>The items <paramref name="target"/> must hold to match <paramref name="wanted"/>, or null when it already does.</summary>
    private static List<T>? PlanSection<T>(List<T> target, SnapshotSection wanted, SnapshotSection current, Action<T>? afterRead = null)
        where T : class
    {
        if (current.Count != target.Count)
        {
            throw new InvalidOperationException("The current snapshot was not captured from this project.");
        }

        if (wanted.ContentEquals(current))
        {
            return null;
        }

        Dictionary<byte[], int>? byContent = null;
        var used = new bool[current.Count];
        var result = new List<T>(wanted.Count);
        for (var i = 0; i < wanted.Count; i++)
        {
            var json = wanted.Items[i];
            var at = -1;
            if (i < current.Count && !used[i] && SnapshotSection.SameBytes(current.Items[i], json))
            {
                at = i;
            }
            else
            {
                byContent ??= BuildContentIndex(current);
                if (byContent.TryGetValue(json, out var found) && !used[found])
                {
                    at = found;
                }
            }

            if (at >= 0)
            {
                used[at] = true;
                result.Add(target[at]);
            }
            else
            {
                var item = JsonSerializer.Deserialize<T>(json, Options)
                    ?? throw new InvalidOperationException("An undo snapshot item could not be read back.");
                afterRead?.Invoke(item);
                result.Add(item);
            }
        }

        return result;
    }

    private static Dictionary<byte[], int> BuildContentIndex(SnapshotSection section)
    {
        var index = new Dictionary<byte[], int>(section.Count, ByteContentComparer.Instance);
        for (var i = 0; i < section.Count; i++)
        {
            index.TryAdd(section.Items[i], i);
        }

        return index;
    }

    /// <summary>The stage to put in, or null to keep the current one.</summary>
    private StageLayout? PlanStage(Project project, ProjectSnapshot current)
    {
        if (SnapshotSection.SameBytes(Stage, current.Stage))
        {
            return null;
        }

        var stage = JsonSerializer.Deserialize<StageLayout>(Stage, Options)
            ?? throw new InvalidOperationException("The undo snapshot's stage could not be read back.");
        var live = project.Stage;
        if (!stage.IsArranged && stage.Placements.Count == 0 && !live.IsArranged && live.Placements.Count > 0 &&
            stage.Width == live.Width && stage.Depth == live.Depth)
        {
            // Automatic placement for viewing (StagePage): not an edit, keep it.
            return null;
        }

        return stage;
    }

    /// <summary>Serializes items into one reused buffer, so comparing an unchanged item with the previous snapshot allocates nothing.</summary>
    private sealed class ItemWriter : IDisposable
    {
        private readonly ArrayBufferWriter<byte> _buffer = new(4096);
        private readonly Utf8JsonWriter _json;

        public ItemWriter()
        {
            _json = new Utf8JsonWriter(_buffer, new JsonWriterOptions { Encoder = Options.Encoder, Indented = false });
        }

        /// <summary>The item's JSON; valid until the next call.</summary>
        public ReadOnlySpan<byte> Write<T>(T item)
        {
            _buffer.ResetWrittenCount();
            _json.Reset(_buffer);
            JsonSerializer.Serialize(_json, item, Options);
            _json.Flush();
            return _buffer.WrittenSpan;
        }

        public void Dispose() => _json.Dispose();
    }
}
