using System.Globalization;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Functions;

/// <summary>
/// Profile-driven scene editing: channels are always found through the fixture's profile mode by
/// <see cref="ChannelType"/>, never by offset.
/// </summary>
public static class SceneEditing
{
    /// <summary>The fixture's channels in DMX order, or empty when its profile or mode is missing.</summary>
    public static IReadOnlyList<ChannelDefinition> ResolveFixtureChannels(this Project project, Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fixture);
        var profile = project.FindProfile(fixture.ProfileId);
        var mode = profile?.FindMode(fixture.ModeName);
        return profile is null || mode is null ? [] : profile.ResolveChannels(mode);
    }

    /// <summary>Whether the fixture's mode has at least one channel of <paramref name="type"/>.</summary>
    public static bool HasChannelType(this Project project, Fixture fixture, ChannelType type) =>
        project.ResolveFixtureChannels(fixture).Any(c => c.Type == type);

    /// <summary>Fixtures the scene sets values for, in first-use order.</summary>
    public static IReadOnlyList<Guid> TargetFixtureIds(this SceneFunction scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Values.Select(v => v.FixtureId).Distinct().ToList();
    }

    /// <summary>
    /// Makes the scene target a fixture: every channel of its mode that the scene does not set yet is stored at the
    /// profile default, so the scene fully defines the fixture's look. Existing values are kept.
    /// </summary>
    public static void AddFixture(this SceneFunction scene, Project project, Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(scene);
        foreach (var channel in project.ResolveFixtureChannels(fixture))
        {
            if (scene.GetValue(fixture.Id, channel.Key) is null)
            {
                scene.SetValue(fixture.Id, channel.Key, channel.Default);
            }
        }
    }

    /// <summary>Removes every value of the fixture from the scene. Returns true when something was removed.</summary>
    public static bool RemoveFixture(this SceneFunction scene, Guid fixtureId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Values.RemoveAll(v => v.FixtureId == fixtureId) > 0;
    }

    /// <summary>
    /// Sets every channel of <paramref name="type"/> in the fixture's mode (e.g. all red cells of a bar) to
    /// <paramref name="value"/>. Returns how many channels were set (0 when the mode has no such channel).
    /// </summary>
    public static int SetChannelType(this SceneFunction scene, Project project, Fixture fixture, ChannelType type, byte value)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var count = 0;
        foreach (var channel in project.ResolveFixtureChannels(fixture))
        {
            if (channel.Type == type)
            {
                scene.SetValue(fixture.Id, channel.Key, value);
                count++;
            }
        }

        return count;
    }

    /// <summary>Whether the scene controls the fixture channel (stores a value for it).</summary>
    public static bool IsChannelIncluded(this SceneFunction scene, Guid fixtureId, string channelKey)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.GetValue(fixtureId, channelKey) is not null;
    }

    /// <summary>
    /// Includes or excludes one fixture channel. Including stores <paramref name="value"/> (or keeps the stored value
    /// when it is null); excluding removes the value so the scene no longer controls that channel.
    /// </summary>
    public static void SetChannelIncluded(this SceneFunction scene, Guid fixtureId, ChannelDefinition channel, bool included, byte? value = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(channel);
        if (!included)
        {
            scene.RemoveValue(fixtureId, channel.Key);
            return;
        }

        if (value is { } level)
        {
            scene.SetValue(fixtureId, channel.Key, level);
        }
        else if (scene.GetValue(fixtureId, channel.Key) is null)
        {
            scene.SetValue(fixtureId, channel.Key, channel.Default);
        }
    }

    /// <summary>
    /// Like <see cref="SetChannelType"/> but only for channels the scene already includes; excluded channels stay
    /// uncontrolled. Returns how many channels were set.
    /// </summary>
    public static int SetIncludedChannelType(this SceneFunction scene, Project project, Fixture fixture, ChannelType type, byte value)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var count = 0;
        foreach (var channel in project.ResolveFixtureChannels(fixture))
        {
            if (channel.Type == type && scene.GetValue(fixture.Id, channel.Key) is not null)
            {
                scene.SetValue(fixture.Id, channel.Key, value);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The stored value of the fixture's first <em>included</em> channel of <paramref name="type"/>; null when the
    /// scene includes no such channel.
    /// </summary>
    public static byte? GetIncludedChannelTypeValue(this SceneFunction scene, Project project, Fixture fixture, ChannelType type)
    {
        ArgumentNullException.ThrowIfNull(scene);
        foreach (var channel in project.ResolveFixtureChannels(fixture))
        {
            if (channel.Type == type && scene.GetValue(fixture.Id, channel.Key) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Converts a slider/text value to a DMX value: rounded, clamped to 0–255; NaN becomes 0.</summary>
    public static byte ToDmxValue(double value) => double.IsNaN(value) ? (byte)0 : ToByte(value);

    /// <summary>
    /// Parses a typed channel value ("128", " 64 ", "12.6"): rounded and clamped to 0–255, so "300" gives 255 and
    /// "-5" gives 0. Returns false for empty, non-numeric or non-finite text, so the caller can revert the box.
    /// The current culture is tried first, then the invariant culture.
    /// </summary>
    public static bool TryParseDmxValue(string? text, out byte value)
    {
        value = 0;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        if ((!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
                && !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = ToByte(parsed);
        return true;
    }

    /// <summary>
    /// The value of the fixture's first channel of <paramref name="type"/>: the scene's value, else the profile
    /// default. Null when the mode has no such channel.
    /// </summary>
    public static byte? GetChannelTypeValue(this SceneFunction scene, Project project, Fixture fixture, ChannelType type)
    {
        ArgumentNullException.ThrowIfNull(scene);
        foreach (var channel in project.ResolveFixtureChannels(fixture))
        {
            if (channel.Type == type)
            {
                return scene.GetValue(fixture.Id, channel.Key) ?? channel.Default;
            }
        }

        return null;
    }

    /// <summary>
    /// The scene's values by fixture and channel key (the first value wins on duplicates, like
    /// <see cref="SceneFunction.GetValue"/>). For callers that read many values at once: <see cref="SceneFunction.GetValue"/>
    /// scans the whole list, so reading every channel of a big scene that way is quadratic. A snapshot: rebuild after edits.
    /// </summary>
    public static Dictionary<(Guid FixtureId, string ChannelKey), byte> IndexValues(this SceneFunction scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var index = new Dictionary<(Guid FixtureId, string ChannelKey), byte>(scene.Values.Count);
        foreach (var value in scene.Values)
        {
            index.TryAdd((value.FixtureId, value.ChannelKey), value.Value);
        }

        return index;
    }

    /// <summary>
    /// Approximate on-screen colour of the fixture under this scene (for previews only, not DMX output): RGB plus
    /// white, amber (≈255,191,0) and UV (≈ violet) mixed additively, scaled by the dimmer when the mode has one.
    /// A dimmer-only fixture shows as warm white at its dimmer level.
    /// </summary>
    public static (byte R, byte G, byte B) PreviewColor(this SceneFunction scene, Project project, Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return PreviewColor(project, fixture, key => scene.GetValue(fixture.Id, key));
    }

    /// <summary>
    /// <see cref="PreviewColor(SceneFunction, Project, Fixture)"/> reading the values from an index built by
    /// <see cref="IndexValues"/> (previewing every light of a big scene).
    /// </summary>
    public static (byte R, byte G, byte B) PreviewColor(
        this IReadOnlyDictionary<(Guid FixtureId, string ChannelKey), byte> sceneValues,
        Project project,
        Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(sceneValues);
        return PreviewColor(project, fixture, key => sceneValues.TryGetValue((fixture.Id, key), out var value) ? value : null);
    }

    private static (byte R, byte G, byte B) PreviewColor(Project project, Fixture fixture, Func<string, byte?> storedValue)
    {
        var channels = project.ResolveFixtureChannels(fixture);

        // The first channel of the type: the scene's value, else the profile default; null when the mode has none.
        double? TypeValue(ChannelType type)
        {
            foreach (var channel in channels)
            {
                if (channel.Type == type)
                {
                    return storedValue(channel.Key) ?? channel.Default;
                }
            }

            return null;
        }

        double Level(ChannelType type) => TypeValue(type) ?? 0;

        var hasColor = channels.Any(c => c.Type.IsColor());
        double r, g, b;
        if (hasColor)
        {
            double white = Level(ChannelType.White), amber = Level(ChannelType.Amber), uv = Level(ChannelType.UV);
            r = Level(ChannelType.Red) + white + amber + (uv * 0.35);
            g = Level(ChannelType.Green) + white + (amber * 0.75);
            b = Level(ChannelType.Blue) + white + uv;
        }
        else
        {
            r = 255;
            g = 236;
            b = 200;
        }

        if (TypeValue(ChannelType.Dimmer) is { } dimmer)
        {
            var scale = dimmer / 255.0;
            r *= scale;
            g *= scale;
            b *= scale;
        }
        else if (!hasColor)
        {
            r = g = b = 0;
        }

        return (ToByte(r), ToByte(g), ToByte(b));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
