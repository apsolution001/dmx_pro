namespace DmxPro.Core.Projects;

/// <summary>
/// File ▸ Open recent: the most recently opened or saved project files, newest first. Pure list logic so it can be
/// tested; the app stores the list in its settings. Paths compare case-insensitively (Windows file system).
/// </summary>
public static class RecentProjectList
{
    /// <summary>How many projects the menu shows.</summary>
    public const int MaxCount = 8;

    /// <summary>
    /// Returns a new list with <paramref name="path"/> first, any earlier entry for the same file removed, blank entries
    /// dropped and the list cut to <see cref="MaxCount"/>.
    /// </summary>
    public static List<string> Add(IEnumerable<string>? list, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = new List<string> { path.Trim() };
        result.AddRange(Normalize(list).Where(p => !SamePath(p, path)));
        return Cap(result);
    }

    /// <summary>Returns a new list without <paramref name="path"/> (e.g. the file no longer exists).</summary>
    public static List<string> Remove(IEnumerable<string>? list, string path) =>
        Normalize(list).Where(p => !SamePath(p, path)).ToList();

    /// <summary>Drops blank and duplicate entries (first one wins) and cuts the list to <see cref="MaxCount"/>.</summary>
    public static List<string> Normalize(IEnumerable<string>? list)
    {
        var result = new List<string>();
        foreach (var entry in list ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var path = entry.Trim();
            if (!result.Any(p => SamePath(p, path)))
            {
                result.Add(path);
            }
        }

        return Cap(result);
    }

    /// <summary>True when both strings name the same file (case-insensitive, ignoring surrounding spaces).</summary>
    public static bool SamePath(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static List<string> Cap(List<string> list)
    {
        if (list.Count > MaxCount)
        {
            list.RemoveRange(MaxCount, list.Count - MaxCount);
        }

        return list;
    }
}
