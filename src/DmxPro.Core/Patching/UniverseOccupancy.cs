using DmxPro.Core.Profiles;

namespace DmxPro.Core.Patching;

/// <summary>
/// Snapshot of which enabled fixtures occupy each address (1–512) of one universe, built once so repeated questions
/// ("next free block of n channels?", "which fixtures would this plan overlap?") cost O(512) or O(planned channels)
/// instead of a pass over every patched fixture. The Add Fixture dialog asks these on every keystroke.
/// <para>
/// It is a snapshot: build a new one after the patch changed. Answers are identical to
/// <see cref="PatchValidator.FindFreeAddress"/> and <see cref="PatchPlan.Create(int, int, int, int, int, IReadOnlyList{Fixture}, IProfileResolver)"/>.
/// </para>
/// </summary>
public sealed class UniverseOccupancy
{
    private readonly List<(Fixture Fixture, FixtureFootprint Footprint)> _entries;

    /// <summary>Indexes into <see cref="_entries"/> per 0-based address; null = free.</summary>
    private readonly List<int>?[] _owners;

    private UniverseOccupancy(int universe, List<(Fixture, FixtureFootprint)> entries, List<int>?[] owners)
    {
        Universe = universe;
        _entries = entries;
        _owners = owners;
    }

    public int Universe { get; }

    /// <summary>
    /// Builds the occupancy of <paramref name="universe"/> from the enabled fixtures whose profile and mode resolve
    /// (others have no known footprint). <paramref name="ignoreFixtureId"/> leaves one fixture out (when moving it).
    /// </summary>
    public static UniverseOccupancy Build(
        IReadOnlyList<Fixture> fixtures,
        IProfileResolver profiles,
        int universe,
        Guid? ignoreFixtureId = null)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(profiles);

        var entries = new List<(Fixture, FixtureFootprint)>();
        var owners = new List<int>?[DmxAddress.UniverseSize];
        foreach (var fixture in fixtures)
        {
            if (!fixture.Enabled || fixture.Universe != universe || fixture.Id == ignoreFixtureId ||
                PatchValidator.GetFootprint(fixture, profiles) is not { } footprint)
            {
                continue;
            }

            var index = entries.Count;
            entries.Add((fixture, footprint));

            // Footprints partly outside 1–512 only occupy the addresses that exist.
            var end = Math.Min(footprint.EndAddress, DmxAddress.Max);
            for (var address = Math.Max(footprint.StartAddress, DmxAddress.Min); address <= end; address++)
            {
                (owners[DmxAddress.ToIndex(address)] ??= []).Add(index);
            }
        }

        return new UniverseOccupancy(universe, entries, owners);
    }

    /// <summary>True when no enabled fixture uses <paramref name="address"/> (1–512).</summary>
    public bool IsFree(int address) => _owners[DmxAddress.ToIndex(address)] is null;

    /// <summary>
    /// The lowest start address where <paramref name="channelCount"/> channels fit without overlapping, or null when
    /// the universe has no such free block.
    /// </summary>
    public int? FindFreeAddress(int channelCount)
    {
        var needed = Math.Max(channelCount, 1);
        var run = 0;
        for (var address = DmxAddress.Min; address <= DmxAddress.Max; address++)
        {
            run = IsFree(address) ? run + 1 : 0;
            if (run == needed)
            {
                return address - needed + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// Fixtures (in patch order) that share at least one address with any of <paramref name="footprints"/> in this
    /// universe. Footprints of other universes are ignored.
    /// </summary>
    public IReadOnlyList<Fixture> FindConflicts(IReadOnlyList<FixtureFootprint> footprints)
    {
        ArgumentNullException.ThrowIfNull(footprints);

        var seen = new HashSet<int>();
        var indexes = new List<int>();
        foreach (var footprint in footprints)
        {
            if (footprint.Universe != Universe)
            {
                continue;
            }

            var end = Math.Min(footprint.EndAddress, DmxAddress.Max);
            for (var address = Math.Max(footprint.StartAddress, DmxAddress.Min); address <= end; address++)
            {
                if (_owners[DmxAddress.ToIndex(address)] is not { } owners)
                {
                    continue;
                }

                foreach (var owner in owners)
                {
                    if (seen.Add(owner))
                    {
                        indexes.Add(owner);
                    }
                }
            }
        }

        indexes.Sort();
        var conflicts = new List<Fixture>(indexes.Count);
        foreach (var index in indexes)
        {
            conflicts.Add(_entries[index].Fixture);
        }

        return conflicts;
    }

    /// <summary>The footprint of a fixture in this snapshot, or null when it is not part of it.</summary>
    public FixtureFootprint? FootprintOf(Fixture fixture)
    {
        foreach (var (entry, footprint) in _entries)
        {
            if (ReferenceEquals(entry, fixture))
            {
                return footprint;
            }
        }

        return null;
    }
}
