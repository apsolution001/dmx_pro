using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// Pure helpers of the chaser step-look panel (no UI state): one-click colours and Off for one light of a step's
/// scene, and the swatch text of a light. Every channel is resolved through the fixture's profile mode by
/// <see cref="ChannelType"/> (never by offset).
/// </summary>
public static class ChaserStepLookEditing
{
    private static readonly ChannelType[] ColorTypes =
    [
        ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Amber, ChannelType.UV,
    ];

    /// <summary>
    /// Sets one light of a step to a colour preset: each of Red/Green/Blue/White/Amber/UV the light's mode has gets the
    /// preset's level. When the light has a dimmer that is at 0, it goes to full so the colour can be seen (same as the
    /// Sequence editor). Returns true when a channel was set.
    /// </summary>
    public static bool ApplyColor(SceneFunction scene, Project project, Fixture fixture, SceneEditorColorPreset preset)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(preset);
        byte[] levels = [preset.Red, preset.Green, preset.Blue, preset.White, preset.Amber, preset.Uv];
        var changed = false;
        for (var i = 0; i < ColorTypes.Length; i++)
        {
            changed |= scene.SetChannelType(project, fixture, ColorTypes[i], levels[i]) > 0;
        }

        if (scene.GetChannelTypeValue(project, fixture, ChannelType.Dimmer) == 0)
        {
            changed |= scene.SetChannelType(project, fixture, ChannelType.Dimmer, 255) > 0;
        }

        return changed;
    }

    /// <summary>Turns one light of a step off: every colour channel and the dimmer at 0. Returns true when a channel was set.</summary>
    public static bool SetOff(SceneFunction scene, Project project, Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var changed = false;
        foreach (var type in ColorTypes)
        {
            changed |= scene.SetChannelType(project, fixture, type, 0) > 0;
        }

        changed |= scene.SetChannelType(project, fixture, ChannelType.Dimmer, 0) > 0;
        return changed;
    }

    /// <summary>"#RRGGBB" (a real light colour, display only).</summary>
    public static string Hex((byte R, byte G, byte B) color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>"PAR 1 · R 255 G 0 B 0": the swatch tooltip, so the colour never carries meaning alone.</summary>
    public static string SwatchToolTip(string lightName, (byte R, byte G, byte B) color) =>
        $"{lightName} · R {color.R} G {color.G} B {color.B}";
}
