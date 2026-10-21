using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Testing;

/// <summary>
/// Patch problems for the Test &amp; Preview warnings strip: per fixture (in patch order) and totals. A snapshot of the
/// project at <see cref="Analyze"/> time; build a new one after edits.
/// </summary>
public sealed class PatchHealth
{
    private readonly Dictionary<Guid, FixtureHealth> _byId;

    private PatchHealth(IReadOnlyList<FixtureHealth> fixtures)
    {
        Fixtures = fixtures;
        _byId = [];
        foreach (var health in fixtures)
        {
            _byId.TryAdd(health.Fixture.Id, health);
        }
    }

    /// <summary>One entry per fixture, in project (patch list) order.</summary>
    public IReadOnlyList<FixtureHealth> Fixtures { get; }

    /// <summary>Fixtures whose profile or mode is missing.</summary>
    public int MissingProfileOrModeCount => Count(h => h.IsMissingProfile || h.IsMissingMode);

    /// <summary>Enabled fixtures sharing addresses with another enabled fixture.</summary>
    public int OverlapCount => Count(h => h.HasOverlap);

    /// <summary>Fixtures with an invalid universe or a footprint past address 512.</summary>
    public int OutOfRangeCount => Count(h => h.ExceedsUniverse || h.HasInvalidUniverse);

    /// <summary>Disabled fixtures.</summary>
    public int DisabledCount => Count(h => h.IsDisabled);

    /// <summary>True when an enabled fixture with a resolvable mode has a Strobe channel.</summary>
    public bool AnyStrobeChannel => Count(h => h.HasStrobeChannel && !h.IsDisabled) > 0;

    /// <summary>The health of one fixture, or null when it is not in the analysed project.</summary>
    public FixtureHealth? Find(Guid fixtureId) => _byId.GetValueOrDefault(fixtureId);

    /// <summary>Analyses every fixture of <paramref name="project"/>.</summary>
    public static PatchHealth Analyze(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var list = new List<FixtureHealth>(project.Fixtures.Count);
        foreach (var fixture in project.Fixtures)
        {
            var profile = project.FindProfile(fixture.ProfileId);
            var mode = profile?.FindMode(fixture.ModeName);
            var health = new FixtureHealth(fixture, profile, mode);
            if (profile is not null && mode is not null)
            {
                foreach (var key in mode.Channels)
                {
                    if (profile.FindChannel(key) is { Type: ChannelType.Strobe })
                    {
                        health.HasStrobeChannel = true;
                        break;
                    }
                }
            }

            list.Add(health);
        }

        // Same rule as the patch validator: only enabled fixtures with a sendable footprint (valid universe, start address
        // and end within 512) can conflict; the others are already reported as out of range. Footprints only overlap
        // within a universe, so compare per universe (patch order kept) instead of all pairs of a big rig.
        var byUniverse = new Dictionary<int, List<FixtureHealth>>();
        foreach (var health in list)
        {
            if (health.IsDisabled
                || health.HasInvalidUniverse
                || health.Footprint is not { } footprint
                || !footprint.FitsInUniverse)
            {
                continue;
            }

            if (!byUniverse.TryGetValue(footprint.Universe, out var members))
            {
                members = [];
                byUniverse.Add(footprint.Universe, members);
            }

            members.Add(health);
        }

        foreach (var members in byUniverse.Values)
        {
            for (var i = 0; i < members.Count; i++)
            {
                var a = members[i];
                var fa = a.Footprint!.Value;
                List<string>? names = null;
                for (var j = 0; j < members.Count; j++)
                {
                    var b = members[j];
                    if (i != j && fa.Overlaps(b.Footprint!.Value))
                    {
                        (names ??= []).Add(b.Fixture.Name);
                    }
                }

                if (names is not null)
                {
                    a.OverlapsWith = names;
                }
            }
        }

        return new PatchHealth(list);
    }

    private int Count(Func<FixtureHealth, bool> predicate)
    {
        var count = 0;
        foreach (var health in Fixtures)
        {
            if (predicate(health))
            {
                count++;
            }
        }

        return count;
    }
}
