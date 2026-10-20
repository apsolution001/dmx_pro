namespace DmxPro.Core.Profiles;

/// <summary>
/// The one shared rule for what a capability (value range) of a Strobe/shutter channel means. Used by the QLC+ importer
/// (to mark resting ranges safe), the engine (strobe-safe resting value, hardware strobe value) and the virtual stage.
/// </summary>
/// <remarks>
/// Decided in this order:
/// <list type="number">
/// <item><see cref="ChannelCapability.Safe"/> = true → <see cref="StrobeRangeKind.Steady"/> (an explicit statement by the profile).</item>
/// <item>A QLC+ capability <see cref="ChannelCapability.Preset"/>: <c>ShutterOpen</c> → Steady, <c>ShutterClose</c> → Closed,
/// anything starting with <c>Strobe</c>/<c>Pulse</c>/<c>Ramp</c> → Strobing. Other presets fall through to the label.</item>
/// <item>The label, case-insensitive. Negated phrases ("no strobe", "strobe off") are removed first. A remaining strobe word
/// (strobe, flash, pulse, ramp, random, lightning, burst, blink, sparkle) → Strobing; otherwise a closed word
/// (closed, close, blackout, LED/lamp off) → Closed; otherwise a steady phrase (open, no function, no strobe, strobe off,
/// no effect, LED/lamp on, "on", "off", steady) → Steady.</item>
/// <item>Anything else → <see cref="StrobeRangeKind.Unknown"/>. Safety rule 7: an unknown range is never used as a
/// confirmed strobing value nor as a confirmed safe resting value.</item>
/// </list>
/// A strobe word wins over a steady word so a mixed label ("open, strobe slow → fast") is never picked as a safe resting value.
/// </remarks>
public static class StrobeCapabilityClassifier
{
    private static readonly string[] NegatedStrobePhrases = ["no strobe", "strobe off", "no strobing", "without strobe"];

    private static readonly string[] StrobeWords =
        ["strobe", "strobing", "flash", "pulse", "ramp", "random", "lightning", "burst", "blink", "sparkle"];

    private static readonly string[] ClosedWords = ["closed", "close", "blackout", "black out", "led off", "lamp off", "light off"];

    private static readonly string[] SteadyPhrases =
        ["open", "no function", "no effect", "no strobe", "strobe off", "no strobing", "without strobe", "led on", "lamp on",
         "light on", "shutter on", "steady", "constant on"];

    /// <summary>Classifies one capability of a Strobe/shutter channel.</summary>
    public static StrobeRangeKind Classify(ChannelCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (capability.Safe)
        {
            return StrobeRangeKind.Steady;
        }

        var byPreset = ClassifyPreset(capability.Preset);
        return byPreset != StrobeRangeKind.Unknown ? byPreset : ClassifyLabel(capability.Label);
    }

    /// <summary>Classifies a QLC+ capability preset name; <see cref="StrobeRangeKind.Unknown"/> when it says nothing about strobing.</summary>
    public static StrobeRangeKind ClassifyPreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return StrobeRangeKind.Unknown;
        }

        preset = preset.Trim();
        if (preset.Equals("ShutterOpen", StringComparison.OrdinalIgnoreCase))
        {
            return StrobeRangeKind.Steady;
        }

        if (preset.Equals("ShutterClose", StringComparison.OrdinalIgnoreCase) ||
            preset.Equals("ShutterClosed", StringComparison.OrdinalIgnoreCase))
        {
            return StrobeRangeKind.Closed;
        }

        return preset.StartsWith("Strobe", StringComparison.OrdinalIgnoreCase) ||
               preset.StartsWith("Pulse", StringComparison.OrdinalIgnoreCase) ||
               preset.StartsWith("Ramp", StringComparison.OrdinalIgnoreCase)
            ? StrobeRangeKind.Strobing
            : StrobeRangeKind.Unknown;
    }

    /// <summary>Classifies a capability label by keywords (see the class remarks).</summary>
    public static StrobeRangeKind ClassifyLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return StrobeRangeKind.Unknown;
        }

        var text = label.Trim().ToLowerInvariant();
        var withoutNegations = text;
        foreach (var phrase in NegatedStrobePhrases)
        {
            withoutNegations = withoutNegations.Replace(phrase, " ", StringComparison.Ordinal);
        }

        if (ContainsAny(withoutNegations, StrobeWords))
        {
            return StrobeRangeKind.Strobing;
        }

        if (ContainsAny(text, ClosedWords))
        {
            return StrobeRangeKind.Closed;
        }

        if (ContainsAny(text, SteadyPhrases) || text is "on" or "off" or "none")
        {
            return StrobeRangeKind.Steady;
        }

        return StrobeRangeKind.Unknown;
    }

    private static bool ContainsAny(string text, string[] words)
    {
        foreach (var word in words)
        {
            if (text.Contains(word, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
