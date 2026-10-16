using System.Text.Json.Serialization;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Projects;

/// <summary>
/// Everything the user builds, saved as one <c>.dmxpro</c> file. Output enable state is deliberately NOT part of the
/// project: output always starts disabled (safety rule 1).
/// </summary>
public sealed class Project : IProfileResolver
{
    public const int CurrentSchemaVersion = 1;
    public const string FileExtension = ".dmxpro";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled show";

    /// <summary>Embedded copies of every profile used, so the project opens on any machine.</summary>
    public List<FixtureProfile> Profiles { get; set; } = [];

    public List<Fixture> Fixtures { get; set; } = [];

    public List<FixtureGroup> Groups { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<int> Universes => Fixtures.Select(f => f.Universe).Append(1).Distinct().Order();

    public FixtureProfile? FindProfile(string profileId) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));

    public Fixture? FindFixture(Guid id) => Fixtures.FirstOrDefault(f => f.Id == id);

    /// <summary>Embeds a copy of <paramref name="profile"/> (replacing an older copy with the same id).</summary>
    public FixtureProfile EmbedProfile(FixtureProfile profile)
    {
        var copy = profile.Clone();
        var index = Profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Profiles[index] = copy;
        }
        else
        {
            Profiles.Add(copy);
        }

        return copy;
    }

    /// <summary>
    /// Patches <paramref name="count"/> fixtures of one profile/mode one after another, starting at
    /// <paramref name="startAddress"/>, with <paramref name="gap"/> unused channels between them. The profile is
    /// embedded in the project. Several fixtures are named "<paramref name="name"/> 1", "… 2", ….
    /// </summary>
    public IReadOnlyList<Fixture> AddFixtures(
        FixtureProfile profile,
        string modeName,
        int count,
        int universe,
        int startAddress,
        string name,
        int gap = 0)
    {
        var mode = profile.FindMode(modeName)
            ?? throw new ArgumentException($"Profile '{profile.DisplayName}' has no mode '{modeName}'.", nameof(modeName));
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        EmbedProfile(profile);
        var added = new List<Fixture>(count);
        var address = startAddress;
        for (var i = 0; i < count; i++)
        {
            var fixture = new Fixture
            {
                Name = count > 1 ? $"{name} {i + 1}" : name,
                ProfileId = profile.Id,
                ModeName = mode.Name,
                Universe = universe,
                StartAddress = address,
            };
            Fixtures.Add(fixture);
            added.Add(fixture);
            address += mode.ChannelCount + gap;
        }

        return added;
    }

    /// <summary>Copies a fixture to the next free address in its universe (same address if the universe is full).</summary>
    public Fixture DuplicateFixture(Guid fixtureId)
    {
        var source = FindFixture(fixtureId) ?? throw new ArgumentException("Fixture not found.", nameof(fixtureId));
        var copy = source.Duplicate();
        copy.Name = $"{source.Name} (copy)";
        var channels = FindProfile(source.ProfileId)?.FindMode(source.ModeName)?.ChannelCount ?? 1;
        copy.StartAddress = PatchValidator.FindFreeAddress(Fixtures, this, source.Universe, channels) ?? source.StartAddress;
        Fixtures.Insert(Fixtures.IndexOf(source) + 1, copy);
        foreach (var group in Groups.Where(g => g.FixtureIds.Contains(source.Id)))
        {
            group.FixtureIds.Add(copy.Id);
        }

        return copy;
    }

    public FixtureGroup CreateGroup(string name, IEnumerable<Guid> fixtureIds)
    {
        var group = new FixtureGroup { Name = name, FixtureIds = fixtureIds.Distinct().ToList() };
        Groups.Add(group);
        return group;
    }

    /// <summary>Removes a fixture and every group reference to it.</summary>
    public bool RemoveFixture(Guid fixtureId)
    {
        foreach (var group in Groups)
        {
            group.FixtureIds.Remove(fixtureId);
        }

        return Fixtures.RemoveAll(f => f.Id == fixtureId) > 0;
    }

    /// <summary>Full consistency check: profiles, patching, and references between entities.</summary>
    public IReadOnlyList<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();
        foreach (var profile in Profiles)
        {
            issues.AddRange(profile.Validate());
        }

        issues.AddRange(PatchValidator.Validate(Fixtures, this));

        var fixtureIds = Fixtures.Select(f => f.Id).ToHashSet();
        foreach (var group in Groups)
        {
            var missing = group.FixtureIds.Count(id => !fixtureIds.Contains(id));
            if (missing > 0)
            {
                issues.Add(ValidationIssue.Warning(
                    "GROUP_MISSING_FIXTURE",
                    $"Group '{group.Name}' references {missing} fixture(s) that no longer exist.",
                    group.Id));
            }
        }

        return issues;
    }
}
