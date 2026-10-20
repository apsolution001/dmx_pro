namespace DmxPro.Core.Shows;

/// <summary>
/// Finding linked music files and planning "Collect music into the project folder". Pure path logic: the file system
/// is passed in (<c>fileExists</c>), so it is testable; copying is done by the app. Paths compare case-insensitively
/// (Windows). Malformed paths never throw: they simply do not resolve.
/// </summary>
public static class ShowAudioPaths
{
    /// <summary>Folder (next to the project file) that "Collect music" copies songs into.</summary>
    public const string MusicFolderName = "Music";

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Where the song is now: the absolute <see cref="ShowAudio.FilePath"/> when it exists, else the
    /// <see cref="ShowAudio.RelativePath"/> resolved against the project file's folder, else the file name inside the
    /// project folder or its <see cref="MusicFolderName"/> folder. Null when not found (the app asks the user to relink).
    /// </summary>
    public static string? Resolve(ShowAudio audio, string? projectFilePath, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (IsFullyQualified(audio.FilePath) && TryFull(audio.FilePath) is { } absolute && fileExists(absolute))
        {
            return absolute;
        }

        if (ProjectFolder(projectFilePath) is not { } folder)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(audio.RelativePath)
            && TryFull(Combine(folder, NormalizeSeparators(audio.RelativePath))) is { } relative
            && fileExists(relative))
        {
            return relative;
        }

        var fileName = FileName(audio.FilePath);
        if (fileName.Length == 0)
        {
            fileName = FileName(audio.RelativePath);
        }

        if (fileName.Length == 0)
        {
            return null;
        }

        if (TryFull(Combine(folder, fileName)) is { } inFolder && fileExists(inFolder))
        {
            return inFolder;
        }

        if (TryFull(Combine(Combine(folder, MusicFolderName), fileName)) is { } inMusic && fileExists(inMusic))
        {
            return inMusic;
        }

        return null;
    }

    /// <summary>
    /// Links <paramref name="audioFilePath"/> to <paramref name="audio"/>: sets the absolute path and the relative path
    /// for <paramref name="projectFilePath"/> (null relative path when not possible).
    /// </summary>
    public static void Link(ShowAudio audio, string audioFilePath, string? projectFilePath)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);

        var full = Path.GetFullPath(audioFilePath);
        audio.FilePath = full;
        audio.RelativePath = RelativeTo(full, projectFilePath);
    }

    /// <summary>Recomputes every show's <see cref="ShowAudio.RelativePath"/> for a new project location (Save As).</summary>
    public static void UpdateRelativePaths(IEnumerable<Show> shows, string projectFilePath)
    {
        ArgumentNullException.ThrowIfNull(shows);
        foreach (var show in shows)
        {
            if (show.Audio is { } audio && IsFullyQualified(audio.FilePath))
            {
                audio.RelativePath = RelativeTo(audio.FilePath, projectFilePath);
            }
        }
    }

    /// <summary>
    /// Save As with a known previous location: every song is first found from the old project location
    /// (<see cref="Resolve"/>), then relinked for the new one, so songs that were only found via the old relative path
    /// keep working. Songs that are not found keep their absolute path and get a relative path computed from it.
    /// </summary>
    public static void UpdateRelativePaths(
        IEnumerable<Show> shows,
        string? oldProjectFilePath,
        string newProjectFilePath,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(shows);
        ArgumentNullException.ThrowIfNull(fileExists);
        foreach (var show in shows)
        {
            if (show.Audio is not { } audio)
            {
                continue;
            }

            if (Resolve(audio, oldProjectFilePath, fileExists) is { } found)
            {
                Link(audio, found, newProjectFilePath);
            }
            else if (IsFullyQualified(audio.FilePath))
            {
                audio.RelativePath = RelativeTo(audio.FilePath, newProjectFilePath);
            }
        }
    }

    /// <summary>
    /// Plans "Collect music": for every show with music found at <c>source</c> (resolved) that is not already inside the
    /// project's <see cref="MusicFolderName"/> folder, the target path there. Songs used by several shows are copied
    /// once; different files with the same name get " (2)", " (3)" … Returns the copies to make (source → target).
    /// </summary>
    public static IReadOnlyList<(string Source, string Target)> PlanCollect(
        IEnumerable<Show> shows,
        string projectFilePath,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(shows);
        ArgumentNullException.ThrowIfNull(fileExists);

        var plan = new List<(string Source, string Target)>();
        if (MusicFolder(projectFilePath) is not { } musicFolder)
        {
            return plan;
        }

        var plannedSources = new HashSet<string>(PathComparer);
        var usedTargets = new HashSet<string>(PathComparer);
        foreach (var show in shows)
        {
            if (show.Audio is not { } audio
                || Resolve(audio, projectFilePath, fileExists) is not { } source
                || IsInside(source, musicFolder)
                || !plannedSources.Add(source))
            {
                continue;
            }

            var target = UniqueTarget(musicFolder, FileName(source), usedTargets, fileExists);
            usedTargets.Add(target);
            plan.Add((source, target));
        }

        return plan;
    }

    /// <summary>
    /// After the app copied the files of <see cref="PlanCollect"/>: links every show whose song was copied to the copy
    /// (absolute and relative path). Call it before the originals could move. Returns how many shows were relinked.
    /// </summary>
    public static int LinkCollected(
        IEnumerable<Show> shows,
        IEnumerable<(string Source, string Target)> copied,
        string projectFilePath,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(shows);
        ArgumentNullException.ThrowIfNull(copied);
        ArgumentNullException.ThrowIfNull(fileExists);

        var targets = new Dictionary<string, string>(PathComparer);
        foreach (var (source, target) in copied)
        {
            targets.TryAdd(source, target);
        }

        var count = 0;
        foreach (var show in shows)
        {
            if (show.Audio is { } audio
                && Resolve(audio, projectFilePath, fileExists) is { } source
                && targets.TryGetValue(source, out var target))
            {
                Link(audio, target, projectFilePath);
                count++;
            }
        }

        return count;
    }

    /// <summary>Path of <paramref name="fullPath"/> relative to the project file's folder, or null (unsaved project, other drive).</summary>
    private static string? RelativeTo(string fullPath, string? projectFilePath)
    {
        if (ProjectFolder(projectFilePath) is not { } folder)
        {
            return null;
        }

        try
        {
            var fileRoot = Path.GetPathRoot(fullPath);
            var folderRoot = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(fileRoot) || !PathComparer.Equals(fileRoot, folderRoot))
            {
                return null;
            }

            var relative = Path.GetRelativePath(folder, fullPath);
            return Path.IsPathRooted(relative) ? null : relative;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string UniqueTarget(string musicFolder, string fileName, HashSet<string> usedTargets, Func<string, bool> fileExists)
    {
        var candidate = Path.Combine(musicFolder, fileName);
        if (!usedTargets.Contains(candidate) && !fileExists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            candidate = Path.Combine(musicFolder, $"{stem} ({n}){extension}");
            if (!usedTargets.Contains(candidate) && !fileExists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsInside(string path, string folder)
    {
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ProjectFolder(string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || TryFull(projectFilePath) is not { } full)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(full);
        return string.IsNullOrEmpty(folder) ? null : folder;
    }

    private static string? MusicFolder(string? projectFilePath) =>
        ProjectFolder(projectFilePath) is { } folder ? TryFull(Combine(folder, MusicFolderName)) : null;

    private static bool IsFullyQualified(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Path.IsPathFullyQualified(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? TryFull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? Combine(string? folder, string part)
    {
        if (folder is null)
        {
            return null;
        }

        try
        {
            return Path.Combine(folder, part);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Relative paths saved on Windows use '\'; accept both separators on every OS.</summary>
    private static string NormalizeSeparators(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    /// <summary>File name after the last '\' or '/' (a Windows path read on Linux still yields its file name).</summary>
    private static string FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var cut = path.LastIndexOfAny(['\\', '/']);
        return (cut < 0 ? path : path[(cut + 1)..]).Trim();
    }
}
