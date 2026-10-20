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
    /// Copies a function right after the original in the list. The copy is always a library function named
    /// "&lt;name&gt; copy" (then "copy 2", …), also when the source is a show's copy.
    /// </summary>
    public static LightFunction DuplicateFunction(this Project project, Guid functionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var source = project.FindFunction(functionId)
            ?? throw new ArgumentException("Function not found.", nameof(functionId));

        var copy = source.CloneAsNew();
        copy.OwnerShowId = null;
        copy.ShowInFunctionList = true;
        copy.Name = UniqueCopyName(project, source.Name);
        project.Functions.Insert(project.Functions.IndexOf(source) + 1, copy);
        return copy;
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
    /// Deletes a function from the Functions page. Library functions and unused show copies are removed; a show copy
    /// still used by its show is only hidden from the list (<see cref="FunctionDeleteResult.HiddenFromList"/>).
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

        project.Functions.Remove(function);
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
