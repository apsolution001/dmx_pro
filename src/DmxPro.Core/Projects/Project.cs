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
