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

    /// <summary>
    /// Checks every fixture: profile and mode exist, universe and address are valid, the footprint fits in 512 channels,
    /// and enabled footprints don't overlap. An overlap is reported on the later fixture, once per earlier fixture it
    /// overlaps, in patch order. Runs in O(fixtures + occupied channels + overlaps): each universe keeps a 512-slot
    /// occupancy table, so a big rig is never compared all-pairs.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(IReadOnlyList<Fixture> fixtures, IProfileResolver profiles)
    {
        var issues = new List<ValidationIssue>();
        var occupancyByUniverse = new Dictionary<int, OverlapIndex>();
        var placed = new List<(Fixture Fixture, FixtureFootprint Footprint)>();
        var overlapping = new List<int>();

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

            // Disabled fixtures send nothing, so they cannot conflict.
            if (!fixture.Enabled)
            {
                continue;
            }

            if (!occupancyByUniverse.TryGetValue(fixture.Universe, out var occupancy))
            {
                occupancy = new OverlapIndex();
                occupancyByUniverse.Add(fixture.Universe, occupancy);
            }

            // Earlier fixtures sharing a channel, in patch order (their index in `placed`).
            occupancy.CollectOwners(footprint, overlapping);
            foreach (var index in overlapping)
            {
                var other = placed[index];
                issues.Add(ValidationIssue.Warning(
                    "ADDRESS_OVERLAP",
                    $"'{fixture.Name}' ({footprint}) overlaps '{other.Fixture.Name}' ({other.Footprint}). " +
                    "Both will receive the same values on the shared channels.",
                    fixture.Id));
            }

            occupancy.Add(footprint, placed.Count);
            placed.Add((fixture, footprint));
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

    /// <summary>
    /// Which placed fixtures (by their index in placement order) occupy each address of one universe. Only footprints
    /// inside 1–512 are added (the validator checks that first).
    /// </summary>
    private sealed class OverlapIndex
    {
        private readonly List<int>?[] _owners = new List<int>?[DmxAddress.UniverseSize];
        private readonly HashSet<int> _seen = [];

        public void Add(FixtureFootprint footprint, int index)
        {
            for (var address = footprint.StartAddress; address <= footprint.EndAddress; address++)
            {
                (_owners[DmxAddress.ToIndex(address)] ??= []).Add(index);
            }
        }

        /// <summary>Fills <paramref name="result"/> with the distinct owners of the footprint's addresses, ascending.</summary>
        public void CollectOwners(FixtureFootprint footprint, List<int> result)
        {
            result.Clear();
            _seen.Clear();
            for (var address = footprint.StartAddress; address <= footprint.EndAddress; address++)
            {
                if (_owners[DmxAddress.ToIndex(address)] is not { } owners)
                {
                    continue;
                }

                foreach (var owner in owners)
                {
                    if (_seen.Add(owner))
                    {
                        result.Add(owner);
                    }
                }
            }

            result.Sort();
        }
    }
}
