using System.Text.Json.Serialization;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;
using StageLayout = DmxPro.Core.Stage.StageLayout;

namespace DmxPro.Core.Projects;

/// <summary>
/// Everything the user builds, saved as one <c>.dmxpro</c> file. Output enable state is deliberately NOT part of the
/// project: output always starts disabled (safety rule 1).
/// </summary>
public sealed partial class Project : IProfileResolver
{
    public const int CurrentSchemaVersion = 1;
    public const string FileExtension = ".dmxpro";

    private StageLayout _stage = new();

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled show";

    /// <summary>Embedded copies of every profile used, so the project opens on any machine.</summary>
    public List<FixtureProfile> Profiles { get; set; } = [];

    public List<Fixture> Fixtures { get; set; } = [];

    /// <summary>
    /// The virtual stage: size and fixture positions (Stage page). Never null: files saved before the stage existed (or
    /// with <c>"stage": null</c>) get an empty default stage.
    /// </summary>
    public StageLayout Stage
    {
        get => _stage;
        set => _stage = value ?? new();
    }

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
    /// Replaces the embedded copy of <paramref name="profile"/> (matched by id) after its layout was edited, so every
    /// fixture using it follows the new layout. Fixtures keep their mode by name; when that mode no longer exists and
    /// the profile has a single mode (user layouts: the mode name is the channel count), they move to it. The patch is
    /// re-validated so footprint changes that now overlap or pass address 512 are reported, never silently applied.
    /// Scene values for channel keys that no longer exist are kept (playback ignores them) and reported.
    /// </summary>
    public ProfileUpdateResult UpdateProfile(FixtureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var embedded = EmbedProfile(profile);

        var affected = new List<Guid>();
        foreach (var fixture in Fixtures)
        {
            if (!string.Equals(fixture.ProfileId, embedded.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (embedded.FindMode(fixture.ModeName) is null && embedded.Modes.Count == 1)
            {
                fixture.ModeName = embedded.Modes[0].Name;
            }

            affected.Add(fixture.Id);
        }

        // Validate with the affected fixtures last: an overlap is reported on the later fixture, so every overlap that
        // involves an updated fixture lands on one of them.
        var affectedSet = affected.ToHashSet();
        var ordered = Fixtures.Where(f => !affectedSet.Contains(f.Id)).Concat(Fixtures.Where(f => affectedSet.Contains(f.Id))).ToList();
        var patchIssues = PatchValidator.Validate(ordered, this)
            .Where(i => i.EntityId is { } id && affectedSet.Contains(id) && i.Code != "PROFILE_UNVERIFIED")
            .ToList();

        var functionIssues = ValidateFunctionsAndShows()
            .Where(i => i.Code is "SCENE_UNKNOWN_CHANNEL" or "SEQUENCE_UNKNOWN_CHANNEL")
            .ToList();

        return new ProfileUpdateResult
        {
            AffectedFixtureIds = affected,
            PatchIssues = patchIssues,
            FunctionIssues = functionIssues,
        };
    }

    /// <summary>
    /// Patches <paramref name="count"/> fixtures of one profile/mode one after another, starting at
    /// <paramref name="startAddress"/>, with <paramref name="gap"/> unused channels between them. The profile is
    /// embedded in the project (see <see cref="EmbedForNewFixtures"/> for a profile that is already embedded). Several
    /// fixtures are named "<paramref name="name"/> 1", "… 2", ….
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
        ArgumentNullException.ThrowIfNull(profile);
        _ = profile.FindMode(modeName)
            ?? throw new ArgumentException($"Profile '{profile.DisplayName}' has no mode '{modeName}'.", nameof(modeName));
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        var embedded = EmbedForNewFixtures(profile, modeName);
        var mode = embedded.FindMode(modeName)!;
        var added = new List<Fixture>(count);
        var address = startAddress;
        for (var i = 0; i < count; i++)
        {
            var fixture = new Fixture
            {
                Name = count > 1 ? $"{name} {i + 1}" : name,
                ProfileId = embedded.Id,
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

    /// <summary>
    /// The embedded profile new fixtures of <paramref name="profile"/> use. The owner's per-project decisions win over
    /// the library copy (safety rule 7: a Verified flag must never flip silently):
    /// <list type="bullet">
    /// <item>Not embedded yet: a copy of <paramref name="profile"/> is embedded.</item>
    /// <item>Already embedded with the same or a newer <see cref="FixtureProfile.Revision"/> and the requested mode: the
    /// embedded copy is kept as it is (layout and Verified flag), so the new lights match the ones already patched.</item>
    /// <item>The library copy is a newer revision (its channels changed) or the embedded copy lacks the requested mode
    /// (never throw or drop the new lights; this is the previous behaviour for that edge case): it replaces
    /// the embedded copy, Verified flag included. The owner's verification was of the old layout, so carrying it over
    /// would present an unchecked channel map as confirmed; a newer layout that was verified in the library stays
    /// verified.</item>
    /// </list>
    /// </summary>
    private FixtureProfile EmbedForNewFixtures(FixtureProfile profile, string modeName)
    {
        var existing = FindProfile(profile.Id);
        if (existing is not null && existing.Revision >= profile.Revision && existing.FindMode(modeName) is not null)
        {
            return existing;
        }

        return EmbedProfile(profile);
    }

    /// <summary>
    /// Copies a fixture to the next free address in its universe (same address if the universe is full). The copy
    /// stays unplaced on the stage until the user places it (placing it automatically would put it exactly on top of
    /// the original); the Stage page offers <see cref="DmxPro.Core.Stage.StageArrange.PlaceUnplaced"/> for that.
    /// </summary>
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

    public FixtureGroup? FindGroup(Guid groupId) => Groups.FirstOrDefault(g => g.Id == groupId);

    /// <summary>First free default name: "Group 1", "Group 2", … (skips names already used, ignoring case).</summary>
    public string NextGroupName()
    {
        for (var n = Groups.Count + 1; ; n++)
        {
            var name = $"Group {n}";
            if (Groups.All(g => !string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    /// <summary>
    /// Appends fixtures to the end of a group, in the given order. Fixtures already in the group or not in the
    /// project are skipped. Returns how many were added.
    /// </summary>
    public int AddToGroup(Guid groupId, IEnumerable<Guid> fixtureIds)
    {
        var group = FindGroup(groupId) ?? throw new ArgumentException("Group not found.", nameof(groupId));
        var added = 0;
        foreach (var id in fixtureIds)
        {
            if (!group.FixtureIds.Contains(id) && FindFixture(id) is not null)
            {
                group.FixtureIds.Add(id);
                added++;
            }
        }

        return added;
    }

    /// <summary>Takes a fixture out of a group; the fixture itself stays patched.</summary>
    public bool RemoveFromGroup(Guid groupId, Guid fixtureId) =>
        FindGroup(groupId)?.FixtureIds.Remove(fixtureId) ?? false;

    /// <summary>
    /// Moves a member <paramref name="delta"/> places within its group (negative = earlier). The order matters for
    /// chasers and effects that run across a group. Clamped to the ends; returns false when nothing moved.
    /// </summary>
    public bool MoveInGroup(Guid groupId, Guid fixtureId, int delta)
    {
        var ids = FindGroup(groupId)?.FixtureIds;
        var index = ids?.IndexOf(fixtureId) ?? -1;
        if (ids is null || index < 0)
        {
            return false;
        }

        var target = Math.Clamp(index + delta, 0, ids.Count - 1);
        if (target == index)
        {
            return false;
        }

        ids.RemoveAt(index);
        ids.Insert(target, fixtureId);
        return true;
    }

    /// <summary>Renames a group. Blank names are rejected (returns false); the name is trimmed.</summary>
    public bool RenameGroup(Guid groupId, string name)
    {
        var group = FindGroup(groupId);
        if (group is null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        group.Name = name.Trim();
        return true;
    }

    /// <summary>Deletes a group. Its fixtures stay patched.</summary>
    public bool DeleteGroup(Guid groupId) => Groups.RemoveAll(g => g.Id == groupId) > 0;

    /// <summary>Groups that contain <paramref name="fixtureId"/>, in project order.</summary>
    public IEnumerable<FixtureGroup> GroupsOf(Guid fixtureId) => Groups.Where(g => g.FixtureIds.Contains(fixtureId));

    /// <summary>Removes a fixture, every group reference to it and its stage placement.</summary>
    public bool RemoveFixture(Guid fixtureId)
    {
        foreach (var group in Groups)
        {
            group.FixtureIds.Remove(fixtureId);
        }

        Stage.Remove(fixtureId);

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

        issues.AddRange(ValidateFunctionsAndShows());
        return issues;
    }
}
