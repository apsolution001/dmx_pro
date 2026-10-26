using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Pure helpers of the sequence editor (no UI state): moving several steps at once, timing for many steps, one-click
/// colours for a light in a step, the held single-step preview and the colour swatches of a step. Every channel is
/// resolved through the fixture's profile mode by <see cref="ChannelType"/> (never by offset).
/// </summary>
public static class SequenceStepEditing
{
    /// <summary>How many light swatches a step row shows; the rest is summarised as "+N".</summary>
    public const int MaxSwatches = 16;

    private static readonly ChannelType[] ColorTypes =
    [
        ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV,
    ];

    /// <summary>
    /// Moves every item of <paramref name="selected"/> one place up (<paramref name="offset"/> &lt; 0) or down
    /// (&gt; 0), keeping their order. Nothing moves (returns false) when one of them is already at that end.
    /// </summary>
    public static bool MoveItems<T>(IList<T> list, IReadOnlySet<T> selected, int offset)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(selected);
        if (offset == 0)
        {
            return false;
        }

        var indexes = new List<int>();
        for (var i = 0; i < list.Count; i++)
        {
            if (selected.Contains(list[i]))
            {
                indexes.Add(i);
            }
        }

        if (indexes.Count == 0)
        {
            return false;
        }

        if (offset < 0)
        {
            if (indexes[0] == 0)
            {
                return false;
            }

            foreach (var index in indexes)
            {
                (list[index - 1], list[index]) = (list[index], list[index - 1]);
            }
        }
        else
        {
            if (indexes[^1] == list.Count - 1)
            {
                return false;
            }

            for (var k = indexes.Count - 1; k >= 0; k--)
            {
                var index = indexes[k];
                (list[index + 1], list[index]) = (list[index], list[index + 1]);
            }
        }

        return true;
    }

    /// <summary>Gives every step the same fade in / hold / fade out (clamped to 0–<see cref="FunctionEditing.MaxSeconds"/>).</summary>
    public static void ApplyTiming(IEnumerable<SequenceStep> steps, double fadeInSeconds, double holdSeconds, double fadeOutSeconds)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var fadeIn = FunctionEditing.Clamp(fadeInSeconds, 0, FunctionEditing.MaxSeconds);
        var hold = FunctionEditing.Clamp(holdSeconds, 0, FunctionEditing.MaxSeconds);
        var fadeOut = FunctionEditing.Clamp(fadeOutSeconds, 0, FunctionEditing.MaxSeconds);
        foreach (var step in steps)
        {
            step.FadeInSeconds = fadeIn;
            step.HoldSeconds = hold;
            step.FadeOutSeconds = fadeOut;
        }
    }

    /// <summary>
    /// A blank step with the timing of <paramref name="timingOf"/> (when given): every channel of every sequence light
    /// at its profile default. Not added to the sequence.
    /// </summary>
    public static SequenceStep CreateBlank(SequenceFunction sequence, Project project, SequenceStep? timingOf)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(project);
        var step = new SequenceStep();
        if (timingOf is not null)
        {
            step.FadeInSeconds = timingOf.FadeInSeconds;
            step.HoldSeconds = timingOf.HoldSeconds;
            step.FadeOutSeconds = timingOf.FadeOutSeconds;
        }

        var scene = step.AsScene();
        foreach (var fixtureId in sequence.FixtureIds)
        {
            if (project.FindFixture(fixtureId) is { } fixture)
            {
                scene.AddFixture(project, fixture);
            }
        }

        return step;
    }

    /// <summary>A copy of the step (values and timing) without its name, for "Add step" and "Duplicate".</summary>
    public static SequenceStep CopyOf(SequenceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var copy = step.Clone();
        copy.Name = null;
        return copy;
    }

    /// <summary>
    /// Sets one light of a step to a colour preset: each of Red/Green/Blue/White/Amber/UV the light's mode has gets the
    /// preset's level. When the light has a dimmer that is at 0, it goes to full so the colour can be seen.
    /// Returns true when a channel was set.
    /// </summary>
    public static bool ApplyColor(SceneFunction stepScene, Project project, Fixture fixture, SceneEditorColorPreset preset)
    {
        ArgumentNullException.ThrowIfNull(stepScene);
        ArgumentNullException.ThrowIfNull(preset);
        byte[] levels = [preset.Red, preset.Green, preset.Blue, preset.White, preset.Amber, preset.Uv];
        var changed = false;
        for (var i = 0; i < ColorTypes.Length; i++)
        {
            changed |= stepScene.SetChannelType(project, fixture, ColorTypes[i], levels[i]) > 0;
        }

        if (stepScene.GetChannelTypeValue(project, fixture, ChannelType.Dimmer) == 0)
        {
            changed |= stepScene.SetChannelType(project, fixture, ChannelType.Dimmer, 255) > 0;
        }

        return changed;
    }

    /// <summary>Turns one light of a step off: every colour channel and the dimmer at 0. Returns true when a channel was set.</summary>
    public static bool SetOff(SceneFunction stepScene, Project project, Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(stepScene);
        var changed = false;
        foreach (var type in ColorTypes)
        {
            changed |= stepScene.SetChannelType(project, fixture, type, 0) > 0;
        }

        changed |= stepScene.SetChannelType(project, fixture, ChannelType.Dimmer, 0) > 0;
        return changed;
    }

    /// <summary>
    /// The live-preview stand-in for "show the selected step": a copy of the step that snaps in (no fades) and holds
    /// for the longest allowed time, so a looping one-step sequence shows it steadily.
    /// </summary>
    public static SequenceStep HeldCopy(SequenceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var copy = step.Clone();
        copy.FadeInSeconds = 0;
        copy.FadeOutSeconds = 0;
        copy.HoldSeconds = FunctionEditing.MaxSeconds;
        return copy;
    }

    /// <summary>
    /// The colour swatches of the first <paramref name="max"/> lights of a step (sequence light order, missing lights
    /// skipped), from the step's values with profile defaults for the rest.
    /// </summary>
    public static IReadOnlyList<SequenceSwatch> Swatches(
        SequenceStep step,
        IReadOnlyList<Guid> fixtureIds,
        IReadOnlyDictionary<Guid, Fixture> fixtures,
        Project project,
        int max = MaxSwatches)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(fixtureIds);
        ArgumentNullException.ThrowIfNull(fixtures);
        var result = new List<SequenceSwatch>(Math.Min(max, fixtureIds.Count));
        if (max <= 0)
        {
            return result;
        }

        var values = step.AsScene().IndexValues();
        foreach (var id in fixtureIds)
        {
            if (!fixtures.TryGetValue(id, out var fixture))
            {
                continue;
            }

            result.Add(Swatch(fixture.Name, values.PreviewColor(project, fixture)));
            if (result.Count == max)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>A swatch for one light: "#RRGGBB" plus "PAR 1 · R 255 G 0 B 0".</summary>
    public static SequenceSwatch Swatch(string lightName, (byte R, byte G, byte B) color) =>
        new(Hex(color), $"{lightName} · R {color.R} G {color.G} B {color.B}");

    /// <summary>"#RRGGBB".</summary>
    public static string Hex((byte R, byte G, byte B) color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
