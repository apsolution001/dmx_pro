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

    /// <summary>Functions (library and show copies) that reference <paramref name="functionId"/>, e.g. before deleting it.</summary>
    public static IReadOnlyList<LightFunction> FunctionsReferencing(this Project project, Guid functionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Functions.Where(f => f.ReferencedFunctionIds().Contains(functionId)).ToList();
    }

    /// <summary>
    /// Warnings for chaser steps / fade scenes that point at missing (or, for fades, non-scene) functions, functions that
    /// end up playing themselves, effect or sequence fixtures that no longer exist, and sequence values for channel keys
    /// the fixture's mode does not have. Playback skips all of these.
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
        }

        return issues;
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
