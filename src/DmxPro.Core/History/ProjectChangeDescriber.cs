namespace DmxPro.Core.History;

/// <summary>
/// Names an undo step from what changed between two snapshots ("Add fixture "Par 1"", "Delete 3 functions",
/// "Edit show "Wedding"") so no edit needs to describe itself. An edit that touches several lists (deleting a fixture
/// also removes it from groups and scenes) is named after the list where items were added or removed, in the order
/// fixtures, functions, shows, groups, console pages, fixture profiles.
/// </summary>
public static class ProjectChangeDescriber
{
    /// <summary>Description used when nothing more specific fits.</summary>
    public const string Fallback = "Change";

    private const int MaxNameLength = 32;

    /// <summary>Short, sentence-case description of the step from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static string Describe(ProjectSnapshot before, ProjectSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        (SnapshotSection Before, SnapshotSection After, string Noun, string Plural)[] sections =
        [
            (before.Fixtures, after.Fixtures, "fixture", "fixtures"),
            (before.Functions, after.Functions, "function", "functions"),
            (before.Shows, after.Shows, "show", "shows"),
            (before.Groups, after.Groups, "group", "groups"),
            (before.ConsolePages, after.ConsolePages, "console page", "console pages"),
            (before.Profiles, after.Profiles, "fixture profile", "fixture profiles"),
        ];

        var diffs = new Diff[sections.Length];
        for (var i = 0; i < sections.Length; i++)
        {
            diffs[i] = Compare(sections[i].Before, sections[i].After);
        }

        // Added or removed items say most about the edit.
        for (var i = 0; i < sections.Length; i++)
        {
            var diff = diffs[i];
            var (_, after2, noun, plural) = sections[i];
            if (diff.Added.Count > 0 && diff.Removed.Count == 0)
            {
                return diff.Added.Count == 1 ? $"Add {noun} {Quote(after2.Names[diff.Added[0]])}" : $"Add {diff.Added.Count} {plural}";
            }

            if (diff.Removed.Count > 0 && diff.Added.Count == 0)
            {
                return diff.Removed.Count == 1
                    ? $"Delete {noun} {Quote(sections[i].Before.Names[diff.Removed[0]])}"
                    : $"Delete {diff.Removed.Count} {plural}";
            }

            if (diff.Removed.Count > 0)
            {
                return $"Change {plural}";
            }
        }

        for (var i = 0; i < sections.Length; i++)
        {
            var diff = diffs[i];
            var (before2, after2, noun, plural) = sections[i];
            if (diff.Modified.Count == 1)
            {
                var (from, to) = diff.Modified[0];
                return string.Equals(before2.Names[from], after2.Names[to], StringComparison.Ordinal)
                    ? $"Edit {noun} {Quote(after2.Names[to])}"
                    : $"Rename {noun} {Quote(before2.Names[from])}";
            }

            if (diff.Modified.Count > 1)
            {
                return $"Edit {diff.Modified.Count} {plural}";
            }
        }

        for (var i = 0; i < sections.Length; i++)
        {
            if (diffs[i].Reordered)
            {
                return $"Reorder {sections[i].Plural}";
            }
        }

        if (!SnapshotSection.SameBytes(before.Stage, after.Stage))
        {
            return "Edit stage";
        }

        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
        {
            return "Rename project";
        }

        return Fallback;
    }

    private static Diff Compare(SnapshotSection before, SnapshotSection after)
    {
        var diff = new Diff();
        if (before.ContentEquals(after))
        {
            return diff;
        }

        var beforeIndex = new Dictionary<string, int>(before.Count, StringComparer.Ordinal);
        for (var i = 0; i < before.Count; i++)
        {
            beforeIndex.TryAdd(before.Keys[i], i);
        }

        var seen = new HashSet<int>();
        for (var i = 0; i < after.Count; i++)
        {
            if (beforeIndex.TryGetValue(after.Keys[i], out var from) && seen.Add(from))
            {
                if (!SnapshotSection.SameBytes(before.Items[from], after.Items[i]))
                {
                    diff.Modified.Add((from, i));
                }
                else if (from != i)
                {
                    diff.Reordered = true;
                }
            }
            else
            {
                diff.Added.Add(i);
            }
        }

        for (var i = 0; i < before.Count; i++)
        {
            if (!seen.Contains(i))
            {
                diff.Removed.Add(i);
            }
        }

        return diff;
    }

    private static string Quote(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "(no name)";
        }

        return trimmed.Length <= MaxNameLength ? $"\"{trimmed}\"" : $"\"{trimmed[..(MaxNameLength - 1)]}…\"";
    }

    private sealed class Diff
    {
        public List<int> Added { get; } = [];

        public List<int> Removed { get; } = [];

        public List<(int From, int To)> Modified { get; } = [];

        public bool Reordered { get; set; }
    }
}
