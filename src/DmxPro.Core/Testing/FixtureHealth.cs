using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;

namespace DmxPro.Core.Testing;

/// <summary>What the Test &amp; Preview page must warn about for one patched fixture.</summary>
public sealed class FixtureHealth
{
    internal FixtureHealth(Fixture fixture, FixtureProfile? profile, FixtureMode? mode)
    {
        Fixture = fixture;
        Profile = profile;
        Mode = mode;
    }

    /// <summary>The fixture.</summary>
    public Fixture Fixture { get; }

    /// <summary>Its profile, or null when the project does not contain it.</summary>
    public FixtureProfile? Profile { get; }

    /// <summary>Its mode, or null when the profile or the mode is missing.</summary>
    public FixtureMode? Mode { get; }

    /// <summary>The profile is not in the project.</summary>
    public bool IsMissingProfile => Profile is null;

    /// <summary>The profile exists but has no mode with the fixture's mode name.</summary>
    public bool IsMissingMode => Profile is not null && Mode is null;

    /// <summary>The fixture is disabled in the patch and sends nothing.</summary>
    public bool IsDisabled => !Fixture.Enabled;

    /// <summary>The universe number is invalid.</summary>
    public bool HasInvalidUniverse => !DmxAddress.IsValidUniverse(Fixture.Universe);

    /// <summary>The start address is outside 1–512 or the footprint runs past 512.</summary>
    public bool ExceedsUniverse => Mode is not null && !Footprint!.Value.FitsInUniverse;

    /// <summary>The fixture is enabled and shares at least one address with another enabled fixture.</summary>
    public bool HasOverlap => OverlapsWith.Count > 0;

    /// <summary>Names of the enabled fixtures it overlaps with.</summary>
    public IReadOnlyList<string> OverlapsWith { get; internal set; } = [];

    /// <summary>The mode contains a Strobe-type channel.</summary>
    public bool HasStrobeChannel { get; internal set; }

    /// <summary>The footprint, or null when profile/mode are missing.</summary>
    public FixtureFootprint? Footprint => Mode is null ? null : new FixtureFootprint(Fixture.Universe, Fixture.StartAddress, Mode.ChannelCount);

    /// <summary>
    /// Why the fixture has no or limited test faders, or null when everything is patched. Missing profile/mode,
    /// disabled and past-512 are reported here; overlaps and unverified profiles are warnings, not blockers.
    /// </summary>
    public string? ProblemText
    {
        get
        {
            if (IsMissingProfile)
            {
                return $"Profile \"{Fixture.ProfileId}\" is missing from the project — no channels to test.";
            }

            if (IsMissingMode)
            {
                return $"Mode \"{Fixture.ModeName}\" is missing from profile {Profile!.DisplayName} — no channels to test.";
            }

            if (IsDisabled)
            {
                return "This light is disabled in Fixtures, so it sends nothing. Enable it to test it.";
            }

            if (HasInvalidUniverse)
            {
                return $"Universe {Fixture.Universe} is invalid — no channels can be sent.";
            }

            if (ExceedsUniverse)
            {
                return $"Channels past address 512 cannot be sent (footprint {Footprint}). Move the light to a lower start address.";
            }

            return null;
        }
    }
}
