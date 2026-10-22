namespace DmxPro.Core.Functions;

/// <summary>
/// Load-time repair of function times. Editors only accept 0–<see cref="FunctionEditing.MaxSeconds"/>, but a damaged or
/// hand-edited file can hold any double (NaN, negative, 1e308); such values are pulled into the editor range so the
/// editors, cycle readouts and the engine all see the same sane numbers. Valid values are left exactly as they are
/// (no rounding).
/// </summary>
public static class FunctionTimes
{
    /// <summary>
    /// Clamps every time of <paramref name="function"/> (fades, holds, durations, periods, offsets) to the editor range:
    /// NaN and negative become the minimum (pulse period: its default for NaN), values above
    /// <see cref="FunctionEditing.MaxSeconds"/> become that maximum.
    /// </summary>
    /// <returns>True when any value changed.</returns>
    public static bool ClampTimes(this LightFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var changed = false;
        switch (function)
        {
            case SceneFunction scene:
                scene.FadeInSeconds = Fix(scene.FadeInSeconds, ref changed);
                scene.FadeOutSeconds = Fix(scene.FadeOutSeconds, ref changed);
                break;

            case ChaserFunction chaser:
                foreach (var step in chaser.Steps)
                {
                    if (step is null)
                    {
                        continue;
                    }

                    step.FadeInSeconds = Fix(step.FadeInSeconds, ref changed);
                    step.HoldSeconds = Fix(step.HoldSeconds, ref changed);
                    step.FadeOutSeconds = Fix(step.FadeOutSeconds, ref changed);
                }

                break;

            case SequenceFunction sequence:
                foreach (var step in sequence.Steps)
                {
                    if (step is null)
                    {
                        continue;
                    }

                    step.FadeInSeconds = Fix(step.FadeInSeconds, ref changed);
                    step.HoldSeconds = Fix(step.HoldSeconds, ref changed);
                    step.FadeOutSeconds = Fix(step.FadeOutSeconds, ref changed);
                }

                break;

            case ColorCycleFunction cycle:
                cycle.HoldSeconds = Fix(cycle.HoldSeconds, ref changed);
                cycle.FadeSeconds = Fix(cycle.FadeSeconds, ref changed);
                cycle.FixtureOffsetSeconds = Fix(cycle.FixtureOffsetSeconds, ref changed);
                break;

            case FadeFunction fade:
                fade.DurationSeconds = Fix(fade.DurationSeconds, ref changed);
                break;

            case FlashFunction flash:
                flash.DurationSeconds = Fix(flash.DurationSeconds, ref changed);
                break;

            case PulseFunction pulse:
                pulse.PeriodSeconds = Fix(
                    pulse.PeriodSeconds,
                    ref changed,
                    PulseFunction.MinPeriodSeconds,
                    PulseFunction.DefaultPeriodSeconds);
                break;
        }

        return changed;
    }

    private static double Fix(double value, ref bool changed, double min = 0, double nanValue = 0)
    {
        var fixedValue = double.IsNaN(value) ? nanValue : Math.Clamp(value, min, FunctionEditing.MaxSeconds);
        if (!fixedValue.Equals(value))
        {
            changed = true;
        }

        return fixedValue;
    }
}
