using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Patching;

/// <summary>Validates fixture patching: profiles exist, addresses fit, footprints don't overlap.</summary>
public static class PatchValidator
{
    /// <summary>
    /// Returns the footprint of <paramref name="fixture"/>, or null when its profile or mode cannot be resolved.
    /// </summary>
    public static FixtureFootprint? GetFootprint(Fixture fixture, IProfileResolver profiles)
    {
        var mode = profiles.FindProfile(fixture.ProfileId)?.FindMode(fixture.ModeName);
        return mode is null ? null : new FixtureFootprint(fixture.Universe, fixture.StartAddress, mode.ChannelCount);
    }

    public static IReadOnlyList<ValidationIssue> Validate(IReadOnlyList<Fixture> fixtures, IProfileResolver profiles)
    {
        var issues = new List<ValidationIssue>();
        var placed = new List<(Fixture Fixture, FixtureFootprint Footprint)>();

        foreach (var fixture in fixtures)
        {
            var profile = profiles.FindProfile(fixture.ProfileId);
            if (profile is null)
            {
                issues.Add(ValidationIssue.Error(
                    "PROFILE_MISSING",
                    $"'{fixture.Name}' uses profile '{fixture.ProfileId}', which is not in the project.",
                    fixture.Id));
                continue;
            }

            var mode = profile.FindMode(fixture.ModeName);
            if (mode is null)
            {
                issues.Add(ValidationIssue.Error(
                    "MODE_MISSING",
                    $"'{fixture.Name}': profile '{profile.DisplayName}' has no mode '{fixture.ModeName}'.",
                    fixture.Id));
                continue;
            }

            if (!DmxAddress.IsValidUniverse(fixture.Universe))
            {
                issues.Add(ValidationIssue.Error(
                    "UNIVERSE_INVALID",
                    $"'{fixture.Name}': universe {fixture.Universe} is invalid (must be 1–{DmxAddress.MaxUniverse}).",
                    fixture.Id));
                continue;
            }

            var footprint = new FixtureFootprint(fixture.Universe, fixture.StartAddress, mode.ChannelCount);
            if (!DmxAddress.IsValid(fixture.StartAddress))
            {
                issues.Add(ValidationIssue.Error(
                    "ADDRESS_OUT_OF_RANGE",
                    $"'{fixture.Name}': start address {fixture.StartAddress} is outside 1–512.",
                    fixture.Id));
                continue;
            }

            if (!footprint.FitsInUniverse)
            {
                issues.Add(ValidationIssue.Error(
                    "FOOTPRINT_EXCEEDS_UNIVERSE",
                    $"'{fixture.Name}' needs {mode.ChannelCount} channels from address {fixture.StartAddress}, " +
                    $"which would end at {footprint.EndAddress} (max 512). Use start address {DmxAddress.Max - mode.ChannelCount + 1} or lower.",
                    fixture.Id));
                continue;
            }

            if (!profile.Verified)
            {
                issues.Add(ValidationIssue.Warning(
                    "PROFILE_UNVERIFIED",
                    $"'{fixture.Name}': profile '{profile.DisplayName}' is not verified. Test each channel at low intensity.",
                    fixture.Id));
            }

            // Disabled fixtures send nothing, so they cannot conflict.
            if (fixture.Enabled)
            {
                foreach (var other in placed.Where(p => p.Footprint.Overlaps(footprint)))
                {
                    issues.Add(ValidationIssue.Warning(
                        "ADDRESS_OVERLAP",
                        $"'{fixture.Name}' ({footprint}) overlaps '{other.Fixture.Name}' ({other.Footprint}). " +
                        "Both will receive the same values on the shared channels.",
                        fixture.Id));
                }

                placed.Add((fixture, footprint));
            }
        }

        return issues;
    }

    /// <summary>
    /// Finds the lowest start address in <paramref name="universe"/> where <paramref name="channelCount"/> channels fit
    /// without overlapping enabled fixtures. Returns null when the universe is full.
    /// </summary>
    public static int? FindFreeAddress(
        IReadOnlyList<Fixture> fixtures,
        IProfileResolver profiles,
        int universe,
        int channelCount,
        Guid? ignoreFixtureId = null)
    {
        var used = fixtures
            .Where(f => f.Enabled && f.Universe == universe && f.Id != ignoreFixtureId)
            .Select(f => GetFootprint(f, profiles))
            .OfType<FixtureFootprint>()
            .OrderBy(f => f.StartAddress)
            .ToList();

        var candidate = DmxAddress.Min;
        foreach (var footprint in used)
        {
            if (candidate + channelCount - 1 < footprint.StartAddress)
            {
                break;
            }

            candidate = Math.Max(candidate, footprint.EndAddress + 1);
        }

        return DmxAddress.EndAddress(candidate, channelCount) <= DmxAddress.Max ? candidate : null;
    }
}
