using DmxPro.Core.Profiles;

namespace DmxPro.Core.Patching;

/// <summary>Which enabled fixtures occupy each address (1–512) of one universe.</summary>
public sealed class AddressMap
{
    private readonly List<Fixture>[] _owners;

    private AddressMap(int universe, List<Fixture>[] owners)
    {
        Universe = universe;
        _owners = owners;
    }

    public int Universe { get; }

    /// <summary>Fixtures at a 1-based address; more than one means an overlap.</summary>
    public IReadOnlyList<Fixture> OwnersAt(int address) => _owners[DmxAddress.ToIndex(address)];

    public int UsedCount => _owners.Count(o => o.Count > 0);

    public int OverlapCount => _owners.Count(o => o.Count > 1);

    public static AddressMap Build(IEnumerable<Fixture> fixtures, IProfileResolver profiles, int universe)
    {
        var owners = new List<Fixture>[DmxAddress.UniverseSize];
        for (var i = 0; i < owners.Length; i++)
        {
            owners[i] = [];
        }

        foreach (var fixture in fixtures.Where(f => f.Enabled && f.Universe == universe))
        {
            if (PatchValidator.GetFootprint(fixture, profiles) is not { } footprint)
            {
                continue;
            }

            var end = Math.Min(footprint.EndAddress, DmxAddress.Max);
            for (var address = Math.Max(footprint.StartAddress, DmxAddress.Min); address <= end; address++)
            {
                owners[DmxAddress.ToIndex(address)].Add(fixture);
            }
        }

        return new AddressMap(universe, owners);
    }
}
