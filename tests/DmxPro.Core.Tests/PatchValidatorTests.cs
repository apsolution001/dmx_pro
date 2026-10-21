using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class PatchValidatorTests
{
    private static Project ProjectWith(params Fixture[] fixtures)
    {
        var project = new Project();
        var profile = project.EmbedProfile(TestProfiles.Rgbw());
        profile.Verified = true;
        project.Fixtures.AddRange(fixtures);
        return project;
    }

    private static Fixture Par(string name, int address, string mode = "8ch", int universe = 1) =>
        new() { Name = name, ProfileId = "test-rgbw", ModeName = mode, StartAddress = address, Universe = universe };

    [Fact]
    public void Validate_AdjacentFixtures_NoIssues()
    {
        var project = ProjectWith(Par("A", 1), Par("B", 9));

        Assert.Empty(PatchValidator.Validate(project.Fixtures, project));
    }

    [Fact]
    public void Validate_OverlappingFixtures_ReportsOverlap()
    {
        var project = ProjectWith(Par("A", 1), Par("B", 8));

        var issue = Assert.Single(PatchValidator.Validate(project.Fixtures, project));
        Assert.Equal("ADDRESS_OVERLAP", issue.Code);
    }

    [Fact]
    public void Validate_SameAddressDifferentUniverse_NoOverlap()
    {
        var project = ProjectWith(Par("A", 1), Par("B", 1, universe: 2));

        Assert.Empty(PatchValidator.Validate(project.Fixtures, project));
    }

    [Fact]
    public void Validate_DisabledFixture_DoesNotOverlap()
    {
        var disabled = Par("B", 1);
        disabled.Enabled = false;
        var project = ProjectWith(Par("A", 1), disabled);

        Assert.Empty(PatchValidator.Validate(project.Fixtures, project));
    }

    [Theory]
    [InlineData(505, true)]
    [InlineData(506, false)]
    public void Validate_FootprintAtUniverseEnd_FitsOnlyUpTo512(int address, bool fits)
    {
        var project = ProjectWith(Par("A", address));

        var issues = PatchValidator.Validate(project.Fixtures, project);

        Assert.Equal(fits, !issues.Any(i => i.Code == "FOOTPRINT_EXCEEDS_UNIVERSE"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(513)]
    public void Validate_AddressOutOfRange_ReportsError(int address)
    {
        var project = ProjectWith(Par("A", address));

        Assert.Contains(PatchValidator.Validate(project.Fixtures, project), i => i.Code == "ADDRESS_OUT_OF_RANGE");
    }

    [Fact]
    public void Validate_MissingProfileAndMode_ReportsBoth()
    {
        var project = ProjectWith(
            new Fixture { Name = "X", ProfileId = "missing", ModeName = "4ch" },
            Par("Y", 1, mode: "99ch"));

        var codes = PatchValidator.Validate(project.Fixtures, project).Select(i => i.Code).ToList();

        Assert.Contains("PROFILE_MISSING", codes);
        Assert.Contains("MODE_MISSING", codes);
    }

    [Fact]
    public void Validate_UnverifiedProfile_NoIssues()
    {
        // Owner decision 2026-10-22: fixtures are checked in QLC+ first, so an unverified profile is not a patch problem.
        var project = ProjectWith(Par("A", 1));
        project.Profiles[0].Verified = false;

        Assert.Empty(PatchValidator.Validate(project.Fixtures, project));
    }

    [Fact]
    public void Validate_FixtureOverlapsSeveralEarlier_ReportsEachOnceInPatchOrder()
    {
        // C (3–10) shares several channels with both A (1–8) and B (5–12): one warning per earlier fixture, A first.
        var project = ProjectWith(Par("A", 1), Par("B", 5), Par("C", 3));

        var messages = PatchValidator.Validate(project.Fixtures, project).Select(i => i.Message).ToList();

        Assert.Equal(3, messages.Count);
        Assert.StartsWith("'B' (U1 5–12) overlaps 'A' (U1 1–8)", messages[0]);
        Assert.StartsWith("'C' (U1 3–10) overlaps 'A' (U1 1–8)", messages[1]);
        Assert.StartsWith("'C' (U1 3–10) overlaps 'B' (U1 5–12)", messages[2]);
    }

    [Fact]
    public void Validate_ManyFixturesOnSameAddress_ReportsEveryPair()
    {
        var project = ProjectWith(Par("A", 1), Par("B", 1), Par("C", 1), Par("D", 1));

        var issues = PatchValidator.Validate(project.Fixtures, project);

        Assert.Equal(6, issues.Count(i => i.Code == "ADDRESS_OVERLAP")); // B:1, C:2, D:3
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Validate_RandomRig_MatchesAllPairsReference(int seed)
    {
        var random = new Random(seed);
        var project = ProjectWith();
        for (var i = 0; i < 300; i++)
        {
            var fixture = Par($"F{i}", random.Next(-2, 516), random.Next(2) == 0 ? "4ch" : "8ch", random.Next(0, 4));
            fixture.Enabled = random.Next(6) != 0;
            if (random.Next(25) == 0)
            {
                fixture.ProfileId = "missing";
            }

            project.Fixtures.Add(fixture);
        }

        var actual = PatchValidator.Validate(project.Fixtures, project);

        Assert.Equal(ReferenceIssues(project), actual.Select(i => (i.Code, i.Message, i.EntityId)));
    }

    [Fact]
    public void FindFreeAddress_GapBetweenFixtures_ReturnsFirstGapThatFits()
    {
        // A: 1–8, B: 13–20 → a 4ch fixture fits at 9, an 8ch one only after B at 21.
        var project = ProjectWith(Par("A", 1), Par("B", 13));

        Assert.Equal(9, PatchValidator.FindFreeAddress(project.Fixtures, project, 1, 4));
        Assert.Equal(21, PatchValidator.FindFreeAddress(project.Fixtures, project, 1, 8));
    }

    [Fact]
    public void FindFreeAddress_UniverseFull_ReturnsNull()
    {
        var project = ProjectWith(Par("A", 1));

        Assert.Null(PatchValidator.FindFreeAddress(project.Fixtures, project, 1, 510));
    }

    [Fact]
    public void ToIndex_Address1_IsIndex0()
    {
        Assert.Equal(0, DmxAddress.ToIndex(1));
        Assert.Equal(511, DmxAddress.ToIndex(512));
        Assert.Throws<ArgumentOutOfRangeException>(() => DmxAddress.ToIndex(0));
    }

    /// <summary>The previous all-pairs overlap check, kept as the reference the faster validator must match exactly.</summary>
    private static List<(string Code, string Message, Guid? EntityId)> ReferenceIssues(Project project)
    {
        var result = new List<(string, string, Guid?)>();
        var placed = new List<(Fixture Fixture, FixtureFootprint Footprint)>();
        foreach (var fixture in project.Fixtures)
        {
            var profile = project.FindProfile(fixture.ProfileId);
            var mode = profile?.FindMode(fixture.ModeName);
            if (profile is null || mode is null || !DmxAddress.IsValidUniverse(fixture.Universe))
            {
                // Same codes as the validator; the message text of these errors is covered elsewhere.
                var code = profile is null ? "PROFILE_MISSING" : mode is null ? "MODE_MISSING" : "UNIVERSE_INVALID";
                result.Add((code, PatchValidator.Validate([fixture], project)[0].Message, fixture.Id));
                continue;
            }

            var footprint = new FixtureFootprint(fixture.Universe, fixture.StartAddress, mode.ChannelCount);
            if (!DmxAddress.IsValid(fixture.StartAddress) || !footprint.FitsInUniverse)
            {
                var single = PatchValidator.Validate([fixture], project)[0];
                result.Add((single.Code, single.Message, fixture.Id));
                continue;
            }

            if (!fixture.Enabled)
            {
                continue;
            }

            foreach (var other in placed.Where(o => o.Footprint.Overlaps(footprint)))
            {
                result.Add((
                    "ADDRESS_OVERLAP",
                    $"'{fixture.Name}' ({footprint}) overlaps '{other.Fixture.Name}' ({other.Footprint}). " +
                    "Both will receive the same values on the shared channels.",
                    fixture.Id));
            }

            placed.Add((fixture, footprint));
        }

        return result;
    }

    [Fact]
    public void Validate_OverlapsInSeveralUniverses_ReportsEachAgainstEarlierFixturesInPatchOrder()
    {
        var project = ProjectWith(
            Par("A", 1),
            Par("B", 1, universe: 2),
            Par("C", 5),
            Par("D", 3),
            Par("E", 8, universe: 2));

        var issues = PatchValidator.Validate(project.Fixtures, project);

        Assert.All(issues, i => Assert.Equal("ADDRESS_OVERLAP", i.Code));
        Assert.Equal(
            ["'C' (U1 5–12) overlaps 'A'", "'D' (U1 3–10) overlaps 'A'", "'D' (U1 3–10) overlaps 'C'", "'E' (U2 8–15) overlaps 'B'"],
            issues.Select(i => i.Message[..i.Message.IndexOf(" (U", i.Message.IndexOf("overlaps", StringComparison.Ordinal), StringComparison.Ordinal)]));
    }
}
