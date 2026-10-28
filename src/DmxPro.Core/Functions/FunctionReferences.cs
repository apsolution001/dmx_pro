using DmxPro.Core.Projects;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Functions;

/// <summary>
/// References between functions (chaser steps, fade scenes) and from functions to fixtures, plus the matching
/// validation (missing references, reference loops, unknown channels).
/// </summary>
public static class FunctionReferences
{
    /// <summary>Ids of the functions <paramref name="function"/> plays (chaser steps, fade from/to scenes), in order.</summary>
    public static IEnumerable<Guid> ReferencedFunctionIds(this LightFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        switch (function)
        {
            case ChaserFunction chaser:
                foreach (var step in chaser.Steps)
                {
                    yield return step.FunctionId;
                }

                break;

            case FadeFunction fade:
                if (fade.FromSceneId is { } from)
                {
                    yield return from;
                }

                if (fade.ToSceneId is { } to)
                {
                    yield return to;
                }

                break;
        }
    }

    /// <summary>Fixtures the function drives directly (scene/sequence values, effect fixture lists), without duplicates.</summary>
    public static IReadOnlyList<Guid> ReferencedFixtureIds(this LightFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        IEnumerable<Guid> ids = function switch
        {
            SceneFunction scene => scene.Values.Select(v => v.FixtureId),
            SequenceFunction sequence => sequence.FixtureIds.Concat(sequence.Steps.SelectMany(s => s.Values).Select(v => v.FixtureId)),
            FixtureEffectFunction effect => effect.FixtureIds,
            _ => [],
        };
        return ids.Distinct().ToList();
    }

    /// <summary>
    /// Functions (library, show copies and chaser-owned step functions) that reference <paramref name="functionId"/>,
    /// e.g. before deleting it. A chaser's own step functions are listed under their chaser (it plays them).
    /// </summary>
    public static IReadOnlyList<LightFunction> FunctionsReferencing(this Project project, Guid functionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var users = new List<LightFunction>();
        foreach (var function in project.Functions)
        {
            if (!function.ReferencedFunctionIds().Contains(functionId))
            {
                continue;
            }

            // A hidden step function means nothing to the owner: name the chaser (or show copy) that plays it.
            var user = TopOwner(project, function);
            if (user.Id != functionId && !users.Contains(user))
            {
                users.Add(user);
            }
        }

        return users;
    }

    /// <summary>The outermost function owning <paramref name="function"/> (itself when not owned); stops on loops or missing owners.</summary>
    private static LightFunction TopOwner(Project project, LightFunction function)
    {
        var seen = new HashSet<Guid> { function.Id };
        var current = function;
        while (current.OwnerFunctionId is { } ownerId && seen.Add(ownerId) && project.FindFunction(ownerId) is { } owner)
        {
            current = owner;
        }

        return current;
    }

    /// <summary>
    /// Warnings for chaser steps / fade scenes that point at missing (or, for fades, non-scene) functions, functions that
    /// end up playing themselves, effect or sequence fixtures that no longer exist, and sequence values for channel keys
    /// the fixture's mode does not have. Playback skips all of these. Also warns about broken step-function ownership
    /// (owner missing or not playing it, two owners, a step function played by another function).
    /// </summary>
    public static IReadOnlyList<ValidationIssue> ValidateFunctionReferences(this Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<ValidationIssue>();
        var byId = new Dictionary<Guid, LightFunction>();
        foreach (var function in project.Functions)
        {
            byId.TryAdd(function.Id, function);
        }

        foreach (var function in project.Functions)
        {
            switch (function)
            {
                case ChaserFunction chaser:
                    var missingSteps = chaser.Steps.Count(s => !byId.ContainsKey(s.FunctionId));
                    if (missingSteps > 0)
                    {
                        issues.Add(ValidationIssue.Warning(
                            "CHASER_MISSING_FUNCTION",
                            $"Chaser '{chaser.Name}' has {missingSteps} step(s) whose function no longer exists.",
                            chaser.Id));
                    }

                    break;

                case FadeFunction fade:
                    foreach (var sceneId in fade.ReferencedFunctionIds())
                    {
                        if (!byId.TryGetValue(sceneId, out var target) || target is not SceneFunction)
                        {
                            issues.Add(ValidationIssue.Warning(
                                "FADE_MISSING_SCENE",
                                $"Fade '{fade.Name}' uses a scene that no longer exists.",
                                fade.Id));
                        }
                    }

                    break;

                case SequenceFunction sequence:
                    ValidateSequenceChannels(project, sequence, issues);
                    break;
            }

            if (function is not SceneFunction)
            {
                // Scene fixture checks live in Project.ValidateFunctionsAndShows (SCENE_MISSING_FIXTURE).
                var missingFixtures = function.ReferencedFixtureIds().Count(id => project.FindFixture(id) is null);
                if (missingFixtures > 0)
                {
                    issues.Add(ValidationIssue.Warning(
                        "FUNCTION_MISSING_FIXTURE",
                        $"Function '{function.Name}' uses {missingFixtures} fixture(s) that no longer exist.",
                        function.Id));
                }
            }

            if (PlaysItself(function, byId))
            {
                issues.Add(ValidationIssue.Warning(
                    "FUNCTION_REFERENCE_LOOP",
                    $"Function '{function.Name}' ends up playing itself; the looping steps are skipped.",
                    function.Id));
            }

            ValidateOwnership(function, byId, issues);
        }

        return issues;
    }

    /// <summary>
    /// Step functions owned by a chaser (<see cref="LightFunction.OwnerFunctionId"/>): the owner must exist and play
    /// them, they have no second owner, and no other function plays them (that would make an edit change both).
    /// </summary>
    private static void ValidateOwnership(LightFunction function, Dictionary<Guid, LightFunction> byId, List<ValidationIssue> issues)
    {
        if (function.OwnerFunctionId is { } ownerId)
        {
            if (function.OwnerShowId is not null)
            {
                issues.Add(ValidationIssue.Warning(
                    "FUNCTION_TWO_OWNERS",
                    $"Function '{function.Name}' belongs to both a show and another function.",
                    function.Id));
            }

            if (!byId.TryGetValue(ownerId, out var owner))
            {
                issues.Add(ValidationIssue.Warning(
                    "FUNCTION_OWNER_MISSING",
                    $"Step function '{function.Name}' belongs to a function that no longer exists.",
                    function.Id));
            }
            else if (!owner.ReferencedFunctionIds().Contains(function.Id))
            {
                issues.Add(ValidationIssue.Warning(
                    "FUNCTION_OWNED_UNUSED",
                    $"Step function '{function.Name}' of '{owner.Name}' is not used by any of its steps.",
                    function.Id));
            }
        }

        foreach (var id in function.ReferencedFunctionIds())
        {
            if (byId.TryGetValue(id, out var target) && target.OwnerFunctionId is { } targetOwner && targetOwner != function.Id
                && byId.TryGetValue(targetOwner, out var owner))
            {
                issues.Add(ValidationIssue.Warning(
                    "FUNCTION_OWNED_SHARED",
                    $"Function '{function.Name}' plays a step of '{owner.Name}'; editing one would change the other.",
                    function.Id));
                return;
            }
        }
    }

    private static void ValidateSequenceChannels(Project project, SequenceFunction sequence, List<ValidationIssue> issues)
    {
        foreach (var value in sequence.Steps.SelectMany(s => s.Values))
        {
            var fixture = project.FindFixture(value.FixtureId);
            var mode = fixture is null ? null : project.FindProfile(fixture.ProfileId)?.FindMode(fixture.ModeName);
            if (fixture is not null && mode is not null && !mode.Channels.Contains(value.ChannelKey, StringComparer.Ordinal))
            {
                issues.Add(ValidationIssue.Warning(
                    "SEQUENCE_UNKNOWN_CHANNEL",
                    $"Sequence '{sequence.Name}' sets channel '{value.ChannelKey}' that fixture '{fixture.Name}' (mode {mode.Name}) does not have.",
                    sequence.Id));
                return;
            }
        }
    }

    /// <summary>True when following references from <paramref name="root"/> leads back to it.</summary>
    private static bool PlaysItself(LightFunction root, Dictionary<Guid, LightFunction> byId)
    {
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>(root.ReferencedFunctionIds());
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (id == root.Id)
            {
                return true;
            }

            if (seen.Add(id) && byId.TryGetValue(id, out var next))
            {
                foreach (var child in next.ReferencedFunctionIds())
                {
                    pending.Push(child);
                }
            }
        }

        return false;
    }
}
