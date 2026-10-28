using DmxPro.Core.Projects;

namespace DmxPro.Core.Functions;

/// <summary>
/// A chaser's own step functions (<see cref="LightFunction.OwnerFunctionId"/>): an "own look" step plays an owned,
/// hidden <see cref="SceneFunction"/>; a step made from another function plays an owned deep copy of it. Owned functions
/// are never listed or offered anywhere else and live and die with their chaser, so two chasers never share one.
/// Older chasers may still have "linked" steps that play a library function directly; those keep working.
/// </summary>
public static class ChaserSteps
{
    /// <summary>
    /// Every function owned by <paramref name="ownerId"/>, recursively (a copied chaser's own steps are included), in
    /// project order. Empty when it owns nothing.
    /// </summary>
    public static IReadOnlyList<LightFunction> OwnedFunctions(this Project project, Guid ownerId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return OwnedIn(project.Functions, ownerId);
    }

    /// <summary>
    /// A new own-look scene for a step of <paramref name="chaserId"/>: owned by the chaser, hidden from the Functions
    /// list, values copied (a later value for the same light and channel wins). It is not added to any project.
    /// </summary>
    public static SceneFunction NewOwnLookScene(Guid chaserId, string name, IEnumerable<SceneValue> values)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(values);
        var scene = new SceneFunction
        {
            Name = name.Trim(),
            OwnerFunctionId = chaserId,
            ShowInFunctionList = false,
        };

        foreach (var value in values)
        {
            if (value is not null && !string.IsNullOrWhiteSpace(value.ChannelKey))
            {
                scene.SetValue(value.FixtureId, value.ChannelKey, value.Value);
            }
        }

        return scene;
    }

    /// <summary>
    /// The chaser's own copy of <paramref name="source"/> for a new step: a deep copy (new id,
    /// <see cref="LightFunction.CopiedFromId"/> = source) owned by <paramref name="chaserId"/> and hidden. For a chaser
    /// source its own step functions are copied too, owned by the copy. Element 0 is the function the step plays.
    /// Nothing is added to the project (the editor commits them with <see cref="CommitChaser"/>).
    /// </summary>
    public static IReadOnlyList<LightFunction> CopyForStep(this Project project, Guid chaserId, LightFunction source)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        var copies = CloneWithOwned(source, project.Functions);
        var copy = copies[0];
        copy.OwnerFunctionId = chaserId;
        copy.OwnerShowId = null;
        copy.ShowInFunctionList = false;
        return copies;
    }

    /// <summary>
    /// A deep copy of <paramref name="root"/> (new id) plus deep copies of every function it owns in the project
    /// (recursively, new ids), with steps and owners re-pointed at the copies. Element 0 is the root copy, whose
    /// ownership and list flags are copied as-is (callers adjust them). Nothing is added to the project.
    /// </summary>
    public static IReadOnlyList<LightFunction> CloneWithOwned(this Project project, LightFunction root)
    {
        ArgumentNullException.ThrowIfNull(project);
        return CloneWithOwned(root, project.Functions);
    }

    /// <summary>
    /// Like <see cref="CloneWithOwned(Project, LightFunction)"/>, but the functions owned by <paramref name="root"/> are
    /// looked up in <paramref name="pool"/> (e.g. an editor's draft step functions that are not in the project yet).
    /// </summary>
    public static IReadOnlyList<LightFunction> CloneWithOwned(LightFunction root, IEnumerable<LightFunction> pool)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(pool);
        var owned = OwnedIn(pool as IReadOnlyList<LightFunction> ?? pool.ToList(), root.Id);

        var newIds = new Dictionary<Guid, Guid>();
        var copies = new List<LightFunction>(owned.Count + 1);
        foreach (var function in owned.Prepend(root))
        {
            var copy = function.CloneAsNew();
            newIds[function.Id] = copy.Id;
            copies.Add(copy);
        }

        foreach (var copy in copies)
        {
            if (!ReferenceEquals(copy, copies[0]) && copy.OwnerFunctionId is { } owner && newIds.TryGetValue(owner, out var newOwner))
            {
                copy.OwnerFunctionId = newOwner;
            }

            Repoint(copy, newIds);
        }

        return copies;
    }

    /// <summary>
    /// Saves a Chaser editor draft: the saved chaser with <paramref name="edited"/>'s id gets its name (a blank name keeps
    /// the old one), steps, run order and direction (its ownership stays as it is). Each function in
    /// <paramref name="owned"/> is stored as a copy, replacing the saved function with the same id or added at the end.
    /// Afterwards every function the chaser owns that no step plays any more is deleted (recursively). Everything is
    /// checked first: on an exception nothing has changed.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="edited">The draft chaser (same id as the saved one).</param>
    /// <param name="owned">
    /// New or changed step functions: each owned by <paramref name="edited"/> (or, for a copied chaser's steps, by
    /// another function in this list or already owned by the chaser).
    /// </param>
    /// <exception cref="ArgumentException">
    /// The chaser does not exist, or a function in <paramref name="owned"/> is not owned by it, also belongs to a show,
    /// is listed twice, or would replace a function the chaser does not own.
    /// </exception>
    public static void CommitChaser(this Project project, ChaserFunction edited, IReadOnlyList<LightFunction> owned)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(owned);
        if (project.FindFunction(edited.Id) is not ChaserFunction saved)
        {
            throw new ArgumentException("Chaser not found.", nameof(edited));
        }

        ValidateOwned(project, edited.Id, owned);

        if (!ReferenceEquals(saved, edited))
        {
            if (!string.IsNullOrWhiteSpace(edited.Name))
            {
                saved.Name = edited.Name.Trim();
            }

            saved.Steps = edited.Steps.Select(s => s.Clone()).ToList();
            saved.RunOrder = edited.RunOrder;
            saved.Direction = edited.Direction;
        }

        foreach (var function in owned)
        {
            var stored = function.CloneAsNew();
            stored.Id = function.Id;
            stored.CopiedFromId = function.CopiedFromId;
            var index = project.Functions.FindIndex(f => f.Id == function.Id);
            if (index >= 0)
            {
                project.Functions[index] = stored;
            }
            else
            {
                project.Functions.Add(stored);
            }
        }

        RemoveUnplayedOwned(project, saved);
    }

    /// <summary>
    /// "Save as function" for a step: adds a library copy of the step's function (deep, with its own step functions;
    /// owners cleared, listed) named <paramref name="name"/> (trimmed; blank = a free name like the step function's).
    /// The step keeps playing its own copy. Works for linked (library) steps too.
    /// </summary>
    /// <exception cref="ArgumentException">The chaser does not exist.</exception>
    /// <exception cref="ArgumentOutOfRangeException">There is no step at <paramref name="stepIndex"/>.</exception>
    /// <exception cref="InvalidOperationException">The step's function no longer exists.</exception>
    public static LightFunction SaveStepAsFunction(this Project project, Guid chaserId, int stepIndex, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.FindFunction(chaserId) is not ChaserFunction chaser)
        {
            throw new ArgumentException("Chaser not found.", nameof(chaserId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(stepIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stepIndex, chaser.Steps.Count);
        var source = project.FindFunction(chaser.Steps[stepIndex].FunctionId)
            ?? throw new InvalidOperationException("The step's function no longer exists.");

        var copies = project.CloneWithOwned(source);
        var copy = copies[0];
        copy.OwnerShowId = null;
        copy.OwnerFunctionId = null;
        copy.ShowInFunctionList = true;
        copy.Name = string.IsNullOrWhiteSpace(name) ? project.SuggestFunctionName(source.Name) : name.Trim();
        project.Functions.AddRange(copies);
        return copy;
    }

    /// <summary>True when the step plays a <see cref="SceneFunction"/> owned by <paramref name="chaserId"/> (an own look).</summary>
    public static bool IsOwnLookStep(this Project project, ChaserStep step, Guid chaserId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(step);
        return project.FindFunction(step.FunctionId) is SceneFunction scene && scene.OwnerFunctionId == chaserId;
    }

    /// <summary>
    /// True when the step plays an existing function that <paramref name="chaserId"/> does not own (a legacy step shared
    /// with the library). False for a missing function.
    /// </summary>
    public static bool IsLinkedStep(this Project project, ChaserStep step, Guid chaserId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(step);
        return project.FindFunction(step.FunctionId) is { } function && function.OwnerFunctionId != chaserId;
    }

    /// <summary>Functions in <paramref name="pool"/> owned (recursively) by <paramref name="ownerId"/>, in pool order.</summary>
    private static List<LightFunction> OwnedIn(IReadOnlyList<LightFunction> pool, Guid ownerId)
    {
        var owners = new HashSet<Guid> { ownerId };

        // Repeat until stable, so the result does not depend on where owners sit in the list.
        bool grew;
        do
        {
            grew = false;
            foreach (var function in pool)
            {
                if (function.OwnerFunctionId is { } owner && owners.Contains(owner) && owners.Add(function.Id))
                {
                    grew = true;
                }
            }
        }
        while (grew);

        var result = new List<LightFunction>();
        foreach (var function in pool)
        {
            if (function.Id != ownerId && function.OwnerFunctionId is { } owner && owners.Contains(owner))
            {
                result.Add(function);
            }
        }

        return result;
    }

    /// <summary>Points references (chaser steps, fade scenes) at the copies made in the same deep copy.</summary>
    private static void Repoint(LightFunction function, Dictionary<Guid, Guid> newIds)
    {
        switch (function)
        {
            case ChaserFunction chaser:
                foreach (var step in chaser.Steps)
                {
                    if (newIds.TryGetValue(step.FunctionId, out var id))
                    {
                        step.FunctionId = id;
                    }
                }

                break;

            case FadeFunction fade:
                if (fade.FromSceneId is { } from && newIds.TryGetValue(from, out var newFrom))
                {
                    fade.FromSceneId = newFrom;
                }

                if (fade.ToSceneId is { } to && newIds.TryGetValue(to, out var newTo))
                {
                    fade.ToSceneId = newTo;
                }

                break;
        }
    }

    private static void ValidateOwned(Project project, Guid chaserId, IReadOnlyList<LightFunction> owned)
    {
        var ids = new HashSet<Guid>();
        foreach (var function in owned)
        {
            if (function is null)
            {
                throw new ArgumentException("A step function is missing.", nameof(owned));
            }

            if (function.Id == chaserId || !ids.Add(function.Id))
            {
                throw new ArgumentException($"Step function '{function.Name}' is listed twice or is the chaser itself.", nameof(owned));
            }
        }

        var alreadyOwned = new HashSet<Guid>();
        foreach (var function in project.OwnedFunctions(chaserId))
        {
            alreadyOwned.Add(function.Id);
        }

        foreach (var function in owned)
        {
            if (function.OwnerShowId is not null)
            {
                throw new ArgumentException($"Step function '{function.Name}' also belongs to a show.", nameof(owned));
            }

            if (function.OwnerFunctionId is not { } owner
                || (owner != chaserId && !ids.Contains(owner) && !alreadyOwned.Contains(owner)))
            {
                throw new ArgumentException($"Step function '{function.Name}' is not owned by this chaser.", nameof(owned));
            }

            if (project.FindFunction(function.Id) is not null && !alreadyOwned.Contains(function.Id))
            {
                throw new ArgumentException($"Step function '{function.Name}' would replace a function this chaser does not own.", nameof(owned));
            }
        }
    }

    /// <summary>Deletes functions owned (recursively) by <paramref name="chaser"/> that no step of their owner plays.</summary>
    private static void RemoveUnplayedOwned(Project project, ChaserFunction chaser)
    {
        var owned = project.OwnedFunctions(chaser.Id);
        if (owned.Count == 0)
        {
            return;
        }

        var byId = new Dictionary<Guid, LightFunction>();
        foreach (var function in owned)
        {
            byId[function.Id] = function;
        }

        var played = new HashSet<Guid>();
        var pending = new Stack<LightFunction>();
        pending.Push(chaser);
        while (pending.Count > 0)
        {
            var function = pending.Pop();
            foreach (var id in function.ReferencedFunctionIds())
            {
                if (byId.TryGetValue(id, out var child) && child.OwnerFunctionId == function.Id && played.Add(id))
                {
                    pending.Push(child);
                }
            }
        }

        project.Functions.RemoveAll(f => byId.ContainsKey(f.Id) && !played.Contains(f.Id));
    }
}
