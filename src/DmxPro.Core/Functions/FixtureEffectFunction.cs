namespace DmxPro.Core.Functions;

/// <summary>
/// Base of the effects that drive fixtures directly by colour/intensity (color cycle, pulse, flash, strobe). The engine
/// resolves each fixture's Red/Green/Blue/White/Amber/UV/Dimmer channels from its profile mode; nothing here knows
/// channel offsets.
/// </summary>
/// <remarks>
/// Colour policy (shared by all effects): R, G and B are sent as given; Amber and UV are sent as 0 so the effect's
/// colour is not tinted by what plays underneath. White is 0 unless <see cref="UseWhiteChannel"/> is on, in which case
/// the common part of R, G and B (their minimum) moves to the White channel. A fixture with White but no R/G/B uses
/// the brightest component on White. A Dimmer channel, when the mode has one, carries the effect level and the colour
/// channels stay at full colour; without a Dimmer the colour channels are scaled by the level instead. A dimmer-only
/// fixture gets the brightest component times the level on its Dimmer.
/// </remarks>
public abstract class FixtureEffectFunction : LightFunction
{
    /// <summary>Fixtures the effect drives, in order (order matters for effects with a per-fixture offset).</summary>
    public List<Guid> FixtureIds { get; set; } = [];

    /// <summary>"Use dedicated white channel for white colours" (spec §5). Off: White stays 0 and RGB mixes white.</summary>
    public bool UseWhiteChannel { get; set; }

    /// <summary>Copies the shared properties into <paramref name="copy"/> (deep copy of the list).</summary>
    protected T CopyEffectTo<T>(T copy)
        where T : FixtureEffectFunction
    {
        ArgumentNullException.ThrowIfNull(copy);
        copy.FixtureIds = [.. FixtureIds];
        copy.UseWhiteChannel = UseWhiteChannel;
        return copy;
    }
}
