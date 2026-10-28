using DmxPro.Core.Functions;

namespace DmxPro.Core.Projects;

/// <summary>
/// Load-time conversion of old <see cref="SequenceFunction"/>s (merged into Chaser on 2026-10-27) into chasers whose
/// steps are own-look scenes owned by the chaser. The chaser keeps the sequence's id, so Virtual Console widgets, show
/// items, other chasers' steps and fades that point at it keep working, and it plays frame-for-frame like the sequence:
/// the step scenes have no fades of their own, the step timing lives on the chaser steps exactly as before.
/// </summary>
public static class SequenceMigration
{
    /// <summary>
    /// Replaces every <see cref="SequenceFunction"/> in <paramref name="project"/> (in place, same list position) by a
    /// <see cref="ChaserFunction"/> with the same id, name, owner (show or function), list flag, run order and direction.
    /// Each sequence step becomes a chaser step with the same fade in / hold / fade out that plays a new hidden scene
    /// owned by the chaser (inserted right after it), holding the step's values, named after the step or "Step N".
    /// </summary>
    /// <returns>The number of sequences converted.</returns>
    public static int Migrate(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var converted = 0;
        for (var i = 0; i < project.Functions.Count; i++)
        {
            if (project.Functions[i] is not SequenceFunction sequence)
            {
                continue;
            }

            var (chaser, scenes) = Convert(sequence);
            project.Functions[i] = chaser;
            project.Functions.InsertRange(i + 1, scenes);
            i += scenes.Count;
            converted++;
        }

        return converted;
    }

    private static (ChaserFunction Chaser, List<LightFunction> Scenes) Convert(SequenceFunction sequence)
    {
        var chaser = new ChaserFunction
        {
            Id = sequence.Id,
            Name = sequence.Name,
            OwnerShowId = sequence.OwnerShowId,
            OwnerFunctionId = sequence.OwnerFunctionId,
            ShowInFunctionList = sequence.ShowInFunctionList,
            CopiedFromId = sequence.CopiedFromId,
            RunOrder = sequence.RunOrder,
            Direction = sequence.Direction,
        };

        var scenes = new List<LightFunction>(sequence.Steps?.Count ?? 0);
        if (sequence.Steps is null)
        {
            return (chaser, scenes);
        }

        for (var index = 0; index < sequence.Steps.Count; index++)
        {
            // A null step (damaged file) cannot be played; it is dropped rather than turned into an empty step.
            if (sequence.Steps[index] is not { } step)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(step.Name) ? $"Step {index + 1}" : step.Name;
            var scene = ChaserSteps.NewOwnLookScene(chaser.Id, name, []);
            CopyValues(step.Values, scene.Values);

            // The step's own fades are on the chaser step; the scene must not add a second fade.
            scene.FadeInSeconds = 0;
            scene.FadeOutSeconds = 0;
            scenes.Add(scene);
            chaser.AddStep(scene.Id, step.HoldSeconds, step.FadeInSeconds, step.FadeOutSeconds);
        }

        return (chaser, scenes);
    }

    /// <summary>
    /// Clones the step values into the scene with the same rules as <see cref="SceneFunction.SetValue"/> (null or keyless
    /// values skipped, a later value for the same light and channel wins in the first one's place), but in one pass:
    /// big shows have sequences with hundreds of steps of hundreds of values each.
    /// </summary>
    private static void CopyValues(List<SceneValue>? source, List<SceneValue> target)
    {
        if (source is null)
        {
            return;
        }

        var positions = new Dictionary<(Guid FixtureId, string ChannelKey), int>(source.Count);
        foreach (var value in source)
        {
            if (value is null || string.IsNullOrWhiteSpace(value.ChannelKey))
            {
                continue;
            }

            var key = (value.FixtureId, value.ChannelKey);
            if (positions.TryGetValue(key, out var position))
            {
                target[position].Value = value.Value;
            }
            else
            {
                positions.Add(key, target.Count);
                target.Add(value.Clone());
            }
        }
    }
}
