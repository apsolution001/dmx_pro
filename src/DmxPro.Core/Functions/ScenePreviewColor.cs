using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Functions;

/// <summary>
/// Approximates the colour a scene shows, for editor swatches and timeline blocks. Channel meanings come from each
/// fixture's profile (never from offsets). Per fixture: Red/Green/Blue add directly, White adds to all three, Amber adds
/// orange, UV adds violet; a Dimmer value scales the result (a dimmer-only fixture counts as white). Fixtures are combined
/// by taking the brightest value per component. This is a display hint only, not what the engine outputs.
/// </summary>
public static class ScenePreviewColor
{
    /// <summary>The preview colour, or null when no stored value resolves to a colour or dimmer channel.</summary>
    /// <remarks>
    /// O(values + fixtures): timeline blocks and library rows call this for every item, and big show projects hold
    /// hundreds of scene copies over hundreds of fixtures, so fixtures are looked up through one dictionary per call
    /// instead of a project scan per fixture.
    /// </remarks>
    public static RgbColor? Estimate(SceneFunction scene, Project project)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(project);
        if (scene.Values.Count == 0)
        {
            return null;
        }

        var fixtures = new Dictionary<Guid, Fixture>(project.Fixtures.Count);
        foreach (var fixture in project.Fixtures)
        {
            fixtures.TryAdd(fixture.Id, fixture);
        }

        // Per fixture, in first-appearance order (same grouping as before; the result is order-independent anyway).
        var mixes = new Dictionary<Guid, Mix?>();
        var order = new List<Mix>();
        foreach (var value in scene.Values)
        {
            if (!mixes.TryGetValue(value.FixtureId, out var mix))
            {
                mix = fixtures.TryGetValue(value.FixtureId, out var fixture) && project.FindProfile(fixture.ProfileId) is { } profile
                    ? new Mix(profile)
                    : null;
                mixes.Add(value.FixtureId, mix);
                if (mix is not null)
                {
                    order.Add(mix);
                }
            }

            mix?.Add(value);
        }

        double red = 0, green = 0, blue = 0;
        var found = false;
        foreach (var mix in order)
        {
            if (mix.Result() is not var (r, g, b))
            {
                continue;
            }

            found = true;
            red = Math.Max(red, r);
            green = Math.Max(green, g);
            blue = Math.Max(blue, b);
        }

        return found ? new RgbColor(ToByte(red), ToByte(green), ToByte(blue)) : null;
    }

    private static byte ToByte(double level) => (byte)Math.Clamp(Math.Round(level * 255), 0, 255);

    /// <summary>One fixture's colour accumulator (components 0..1).</summary>
    private sealed class Mix(FixtureProfile profile)
    {
        private double _r;
        private double _g;
        private double _b;
        private double? _dimmer;
        private bool _hasColor;

        public void Add(SceneValue value)
        {
            var level = value.Value / 255.0;
            switch (profile.FindChannel(value.ChannelKey)?.Type)
            {
                case ChannelType.Red:
                    _r += level;
                    break;
                case ChannelType.Green:
                    _g += level;
                    break;
                case ChannelType.Blue:
                    _b += level;
                    break;
                case ChannelType.White:
                    _r += level;
                    _g += level;
                    _b += level;
                    break;
                case ChannelType.Amber:
                    _r += level;
                    _g += level * 0.75;
                    break;
                case ChannelType.UV:
                    _r += level * 0.4;
                    _b += level * 0.8;
                    break;
                case ChannelType.Dimmer:
                    _dimmer = level;
                    return;
                default:
                    return;
            }

            _hasColor = true;
        }

        /// <summary>The fixture's colour, or null when none of its values is a colour or dimmer.</summary>
        public (double R, double G, double B)? Result()
        {
            if (!_hasColor)
            {
                return _dimmer is { } d ? (d, d, d) : null;
            }

            var scale = _dimmer ?? 1.0;
            return (Math.Min(1, _r) * scale, Math.Min(1, _g) * scale, Math.Min(1, _b) * scale);
        }
    }
}
