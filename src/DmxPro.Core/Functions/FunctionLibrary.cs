using DmxPro.Core.Projects;

namespace DmxPro.Core.Functions;

/// <summary>
/// Functions-page editing of <see cref="Project.Functions"/>: create, duplicate, rename and delete.
/// Shows only ever reference their own copies, so deleting a library function never breaks a show; a show copy that a
/// show still uses is only hidden from the list, never deleted.
/// </summary>
public static class FunctionLibrary
{
    /// <summary>Creates an empty library scene with a unique name ("Scene 1", "Scene 2", …) unless one is given.</summary>
    public static SceneFunction CreateScene(this Project project, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var scene = new SceneFunction
        {
            Name = string.IsNullOrWhiteSpace(name) ? project.NextFunctionName("Scene") : name.Trim(),
        };
        project.Functions.Add(scene);
        return scene;
    }

    /// <summary>
    /// Creates an empty library function of any type with its default settings and a unique name ("Chaser 1", …)
    /// unless one is given.
    /// </summary>
    public static LightFunction CreateFunction(this Project project, FunctionType type, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var function = FunctionFactory.Create(type);
        function.Name = string.IsNullOrWhiteSpace(name)
            ? project.NextFunctionName(FunctionFactory.DefaultBaseName(type))
            : name.Trim();
        project.Functions.Add(function);
        return function;
    }

    /// <summary>First free "<paramref name="baseName"/> N" (N from 1) among all function names, case-insensitive.</summary>
    public static string NextFunctionName(this Project project, string baseName)
    {
        ArgumentNullException.ThrowIfNull(project);
        var names = project.Functions.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var candidate = $"{baseName} {n}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Copies a function right after the original in the list. The copy is always an independent library function
    /// (deep copy, new id), also when the source is a show's copy ("Save as function" in a show): the source is left
    /// unchanged. It is named <paramref name="name"/> (trimmed) when given, else "&lt;name&gt; copy" (then "copy 2", …).
    /// A chaser's own step functions are deep-copied too (owned by the copy, inserted right after it), so the two chasers
    /// never share a step function. A step function itself (owned by a chaser) duplicates as a library function.
    /// </summary>
    public static LightFunction DuplicateFunction(this Project project, Guid functionId, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var source = project.FindFunction(functionId)
            ?? throw new ArgumentException("Function not found.", nameof(functionId));

        var copies = project.CloneWithOwned(source);
        var copy = copies[0];
        copy.OwnerShowId = null;
        copy.OwnerFunctionId = null;
        copy.ShowInFunctionList = true;
        copy.Name = string.IsNullOrWhiteSpace(name) ? UniqueCopyName(project, source.Name) : name.Trim();
        project.Functions.InsertRange(project.Functions.IndexOf(source) + 1, copies);
        return copy;
    }

    /// <summary>
    /// Name to suggest when saving something as a new library function: <paramref name="name"/> (trimmed) when no
    /// function in the Functions list has it (case-insensitive; hidden show copies do not count), else "&lt;name&gt; 2",
    /// "&lt;name&gt; 3", …
    /// </summary>
    public static string SuggestFunctionName(this Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return project.NextFunctionName("Function");
        }

        var names = project.FunctionListItems.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(trimmed))
        {
            return trimmed;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{trimmed} {n}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>True when a function in the Functions list (hidden show copies excluded) has this name, case-insensitive.</summary>
    public static bool IsFunctionNameTaken(this Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length > 0
            && project.FunctionListItems.Any(f => string.Equals(f.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a library scene from fixed channel values (e.g. the Test &amp; Preview page's test values). Values for
    /// lights that no longer exist, or channel keys their profile mode does not have, are skipped (profile-driven: the
    /// scene never stores a channel the light cannot play). Later values for the same channel win. Fade times are
    /// clamped to 0–<see cref="FunctionEditing.MaxSeconds"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public static SceneFunction CreateSceneFromValues(
        this Project project,
        string name,
        IEnumerable<SceneValue> values,
        double fadeInSeconds = 0,
        double fadeOutSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(values);

        var scene = new SceneFunction
        {
            Name = name.Trim(),
            FadeInSeconds = FunctionEditing.Clamp(fadeInSeconds, 0, FunctionEditing.MaxSeconds),
            FadeOutSeconds = FunctionEditing.Clamp(fadeOutSeconds, 0, FunctionEditing.MaxSeconds),
        };

        foreach (var value in values)
        {
            if (value is null
                || string.IsNullOrWhiteSpace(value.ChannelKey)
                || project.FindFixture(value.FixtureId) is not { } fixture)
            {
                continue;
            }

            // A missing profile or mode is reported by the patch validator; the key cannot be checked then.
            var mode = project.FindProfile(fixture.ProfileId)?.FindMode(fixture.ModeName);
            if (mode is not null && !mode.Channels.Contains(value.ChannelKey, StringComparer.Ordinal))
            {
                continue;
            }

            scene.SetValue(value.FixtureId, value.ChannelKey, value.Value);
        }

        project.Functions.Add(scene);
        return scene;
    }

    /// <summary>Renames a function. Returns false (and changes nothing) for a blank name or an unknown id.</summary>
    public static bool RenameFunction(this Project project, Guid functionId, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(name) || project.FindFunction(functionId) is not { } function)
        {
            return false;
        }

        function.Name = name.Trim();
        return true;
    }

    /// <summary>True when an item of any show references the function.</summary>
    public static bool IsUsedByShow(this Project project, Guid functionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Shows.Any(s => s.AllItems.Any(i => i.FunctionId == functionId));
    }

    /// <summary>
    /// Deletes a function from the Functions page. Library functions and unused show copies are removed together with the
    /// functions they own (a chaser's step functions, recursively); a show copy still used by its show is only hidden
    /// from the list (<see cref="FunctionDeleteResult.HiddenFromList"/>) and keeps its step functions.
    /// </summary>
    public static FunctionDeleteResult DeleteFunction(this Project project, Guid functionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.FindFunction(functionId) is not { } function)
        {
            return FunctionDeleteResult.NotFound;
        }

        if (project.IsUsedByShow(functionId))
        {
            function.ShowInFunctionList = false;
            return FunctionDeleteResult.HiddenFromList;
        }

        project.RemoveFunctionWithOwned(function);
        return FunctionDeleteResult.Deleted;
    }

    private static string UniqueCopyName(Project project, string name)
    {
        var names = project.Functions.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{name} copy";
        for (var n = 2; names.Contains(candidate); n++)
        {
            candidate = $"{name} copy {n}";
        }

        return candidate;
    }
}
