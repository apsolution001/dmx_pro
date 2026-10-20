using System.Globalization;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Functions;

/// <summary>
/// Editor helpers for the non-scene function types: typed-number parsing, step reordering, cycle-time readouts,
/// loop-safe chaser step candidates and sequence fixture/step editing. Channel values are always addressed by profile
/// channel key (never offsets).
/// </summary>
public static class FunctionEditing
{
    /// <summary>Longest time any editor field accepts (one hour).</summary>
    public const double MaxSeconds = 3600;

    /// <summary>Shortest step (fade in + hold) the engine plays; shorter steps are stretched to this.</summary>
    public const double MinStepSeconds = 0.05;

    /// <summary>
    /// Parses a typed number ("1.5", " 2 ", "0,5" in a comma culture): rounded to 3 decimals and clamped to
    /// [<paramref name="min"/>, <paramref name="max"/>]. Returns false for empty, non-numeric or non-finite text, so
    /// the caller can revert the box. The current culture is tried first, then the invariant culture.
    /// </summary>
    public static bool TryParseNumber(string? text, double min, double max, out double value)
    {
        value = 0;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        // Accept a trailing unit the readouts show ("1.5 s", "5 Hz", "50 %").
        foreach (var unit in new[] { "hz", "s", "%" })
        {
            if (trimmed.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^unit.Length].TrimEnd();
                break;
            }
        }

        if ((!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
                && !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = Clamp(parsed, min, max);
        return true;
    }

    /// <summary>Rounds to 3 decimals and clamps; NaN becomes <paramref name="min"/>.</summary>
    public static double Clamp(double value, double min, double max) =>
        double.IsNaN(value) ? min : Math.Clamp(Math.Round(value, 3), min, max);

    /// <summary>
    /// Moves the item at <paramref name="index"/> by <paramref name="offset"/> places, stopping at the ends.
    /// Returns the item's new index (unchanged when it cannot move or the index is out of range).
    /// </summary>
    public static int MoveItem<T>(IList<T> list, int index, int offset)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (index < 0 || index >= list.Count)
        {
            return index;
        }

        var target = Math.Clamp(index + offset, 0, list.Count - 1);
        if (target == index)
        {
            return index;
        }

        var item = list[index];
        list.RemoveAt(index);
        list.Insert(target, item);
        return target;
    }

    /// <summary>Time of one full pass of a chaser, as the engine plays it (see the step-list overload).</summary>
    public static double CycleSeconds(this ChaserFunction chaser)
    {
        ArgumentNullException.ThrowIfNull(chaser);
        return CycleSeconds(chaser.Steps.Select(s => (s.FadeInSeconds, s.HoldSeconds, s.FadeOutSeconds)).ToList(), chaser.RunOrder, chaser.Direction);
    }

    /// <summary>Time of one full pass of a sequence, as the engine plays it (see the step-list overload).</summary>
    public static double CycleSeconds(this SequenceFunction sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        return CycleSeconds(sequence.Steps.Select(s => (s.FadeInSeconds, s.HoldSeconds, s.FadeOutSeconds)).ToList(), sequence.RunOrder, sequence.Direction);
    }

    /// <summary>
    /// How long one pass of a step list takes, the way the engine plays it: each played step lasts fade in + hold
    /// (at least <see cref="MinStepSeconds"/>); its fade-out overlaps the next step. Loop: one pass through all steps.
    /// Ping-pong: there and back (end steps not repeated). Single shot: until the last fade-out has finished.
    /// 0 for no steps. Negative or non-finite times count as 0.
    /// </summary>
    public static double CycleSeconds(IReadOnlyList<(double FadeIn, double Hold, double FadeOut)> steps, RunOrder order, RunDirection direction)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var count = steps.Count;
        if (count == 0)
        {
            return 0;
        }

        var playOrder = new List<int>(count * 2);
        for (var i = 0; i < count; i++)
        {
            playOrder.Add(direction == RunDirection.Backward ? count - 1 - i : i);
        }

        if (order == RunOrder.PingPong && count >= 3)
        {
            for (var i = count - 2; i >= 1; i--)
            {
                playOrder.Add(playOrder[i]);
            }
        }

        double time = 0, end = 0;
        foreach (var index in playOrder)
        {
            var (fadeIn, hold, fadeOut) = steps[index];
            var length = Math.Max(Sanitize(fadeIn) + Sanitize(hold), MinStepSeconds);
            end = Math.Max(end, time + length + Sanitize(fadeOut));
            time += length;
        }

        return order == RunOrder.SingleShot ? end : time;
    }

    /// <summary>
    /// Whether a chaser (<paramref name="chaserId"/>) may get a step that plays <paramref name="candidate"/>: not the
    /// chaser itself, and not a function that (through its own steps / scenes) ends up playing the chaser, which would
    /// make a loop.
    /// </summary>
    public static bool CanPlayInChaser(this Project project, Guid chaserId, LightFunction candidate)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Id == chaserId)
        {
            return false;
        }

        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>(candidate.ReferencedFunctionIds());
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (id == chaserId)
            {
                return false;
            }

            if (seen.Add(id) && project.FindFunction(id) is { } next)
            {
                foreach (var child in next.ReferencedFunctionIds())
                {
                    pending.Push(child);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Adds a fixture to the sequence: appended to <see cref="SequenceFunction.FixtureIds"/>, and every step gets the
    /// fixture's channels it does not set yet at their profile defaults. Returns false when it was already there.
    /// </summary>
    public static bool AddSequenceFixture(this SequenceFunction sequence, Project project, Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(fixture);
        if (sequence.FixtureIds.Contains(fixture.Id))
        {
            return false;
        }

        sequence.FixtureIds.Add(fixture.Id);
        foreach (var step in sequence.Steps)
        {
            step.AsScene().AddFixture(project, fixture);
        }

        return true;
    }

    /// <summary>Removes a fixture and all its step values. Returns false when it was not in the sequence.</summary>
    public static bool RemoveSequenceFixture(this SequenceFunction sequence, Guid fixtureId)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        var removed = sequence.FixtureIds.Remove(fixtureId);
        foreach (var step in sequence.Steps)
        {
            removed |= step.Values.RemoveAll(v => v.FixtureId == fixtureId) > 0;
        }

        return removed;
    }

    /// <summary>
    /// Adds a step. With <paramref name="copyOf"/> the new step is a copy of that step (values and timing) inserted
    /// right after it; otherwise it is appended with every channel of every sequence fixture at its profile default.
    /// </summary>
    public static SequenceStep AddSequenceStep(this SequenceFunction sequence, Project project, SequenceStep? copyOf = null)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(project);
        SequenceStep step;
        if (copyOf is not null && sequence.Steps.IndexOf(copyOf) is var index and >= 0)
        {
            step = copyOf.Clone();
            step.Name = null;
            sequence.Steps.Insert(index + 1, step);
            return step;
        }

        step = new SequenceStep();
        var scene = step.AsScene();
        foreach (var fixtureId in sequence.FixtureIds)
        {
            if (project.FindFixture(fixtureId) is { } fixture)
            {
                scene.AddFixture(project, fixture);
            }
        }

        sequence.Steps.Add(step);
        return step;
    }

    /// <summary>
    /// A scene that shares this step's value list (same <see cref="List{T}"/> instance), so the scene editing helpers
    /// (<see cref="SceneEditing"/>: include/exclude channels, set a channel type, add/remove a fixture) work directly
    /// on the step. Fade times of the returned scene mean nothing.
    /// </summary>
    public static SceneFunction AsScene(this SequenceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new SceneFunction { Values = step.Values };
    }

    private static double Sanitize(double seconds) => double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
}
