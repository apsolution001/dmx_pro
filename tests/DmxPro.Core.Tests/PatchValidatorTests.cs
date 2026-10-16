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
    public void Validate_UnverifiedProfile_Warns()
    {
        var project = ProjectWith(Par("A", 1));
        project.Profiles[0].Verified = false;

        Assert.Contains(PatchValidator.Validate(project.Fixtures, project), i => i.Code == "PROFILE_UNVERIFIED");
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
}
