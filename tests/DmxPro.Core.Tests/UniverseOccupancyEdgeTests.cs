using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class UniverseOccupancyEdgeTests
{
    private static Project ProjectWith(params Fixture[] fixtures)
    {
        var project = new Project();
        project.EmbedProfile(TestProfiles.Rgbw());
        project.Fixtures.AddRange(fixtures);
        return project;
    }

    private static Fixture Par(string name, int address, string mode = "8ch", int universe = 1, bool enabled = true) =>
        new() { Name = name, ProfileId = "test-rgbw", ModeName = mode, StartAddress = address, Universe = universe, Enabled = enabled };

    private static UniverseOccupancy Occupancy(Project project, int universe = 1) =>
        UniverseOccupancy.Build(project.Fixtures, project, universe);

    [Fact]
    public void IsFree_Address1AndAddress512_ReflectOccupancy()
    {
        var project = ProjectWith(Par("first", 1, "4ch"), Par("last", 509, "4ch"));
        var occupancy = Occupancy(project);

        Assert.False(occupancy.IsFree(1));
        Assert.False(occupancy.IsFree(4));
        Assert.True(occupancy.IsFree(5));
        Assert.True(occupancy.IsFree(508));
        Assert.False(occupancy.IsFree(509));
        Assert.False(occupancy.IsFree(512));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(513)]
    public void IsFree_AddressOutsideUniverse_Throws(int address)
    {
        var occupancy = Occupancy(ProjectWith());

        Assert.Throws<ArgumentOutOfRangeException>(() => occupancy.IsFree(address));
    }

    [Fact]
    public void FindFreeAddress_FixtureEndsExactlyAt512_NothingFitsAfterIt()
    {
        var project = ProjectWith(Par("tail", 505, "8ch"));
        var occupancy = Occupancy(project);

        Assert.Equal(1, occupancy.FindFreeAddress(504));
        Assert.Null(occupancy.FindFreeAddress(505));
    }

    [Fact]
    public void FindFreeAddress_FixtureStartsAtOne_FirstFreeIsAfterIt()
    {
        var occupancy = Occupancy(ProjectWith(Par("head", 1, "8ch")));

        Assert.Equal(9, occupancy.FindFreeAddress(1));
        Assert.Equal(9, occupancy.FindFreeAddress(504));
        Assert.Null(occupancy.FindFreeAddress(505));
    }

    [Fact]
    public void FindFreeAddress_FullUniverse_ReturnsNull()
    {
        var project = ProjectWith();
        for (var address = 1; address <= 505; address += 8)
        {
            project.Fixtures.Add(Par("p" + address, address, "8ch"));
        }

        var occupancy = Occupancy(project);

        Assert.Null(occupancy.FindFreeAddress(1));
        Assert.Null(PatchValidator.FindFreeAddress(project.Fixtures, project, 1, 1));
    }

    [Fact]
    public void FindFreeAddress_FootprintCrossing512_OccupiesOnlyExistingAddresses()
    {
        // 8ch at 508 would end at 515; the part beyond 512 does not exist, the rest still blocks.
        var project = ProjectWith(Par("overhang", 508, "8ch"));
        var occupancy = Occupancy(project);

        Assert.False(occupancy.IsFree(508));
        Assert.False(occupancy.IsFree(512));
        Assert.Equal(1, occupancy.FindFreeAddress(507));
        Assert.Null(occupancy.FindFreeAddress(508));
        Assert.Equal(
            PatchValidator.FindFreeAddress(project.Fixtures, project, 1, 507),
            occupancy.FindFreeAddress(507));
    }

    [Fact]
    public void FindFreeAddress_FootprintStartingAtZeroOrNegative_BlocksFromAddress1()
    {
        var project = ProjectWith(Par("below", 0, "4ch"), Par("far below", -10, "4ch"));
        var occupancy = Occupancy(project);

        Assert.False(occupancy.IsFree(1));
        Assert.False(occupancy.IsFree(3));
        Assert.True(occupancy.IsFree(4));
        Assert.Equal(PatchValidator.FindFreeAddress(project.Fixtures, project, 1, 2), occupancy.FindFreeAddress(2));
    }

    [Fact]
    public void FindFreeAddress_ChannelCountZero_MatchesPatchValidator()
    {
        // Regression: a zero-width request fits anywhere, as in PatchValidator.FindFreeAddress.
        var project = ProjectWith(Par("a", 1, "4ch"));
        var occupancy = Occupancy(project);

        Assert.Equal(PatchValidator.FindFreeAddress(project.Fixtures, project, 1, 0), occupancy.FindFreeAddress(0));
    }

    [Fact]
    public void Build_SeveralUniverses_OnlyRequestedUniverseCounts()
    {
        var project = ProjectWith(Par("u1", 1, "4ch", universe: 1), Par("u2", 1, "8ch", universe: 2), Par("u3", 20, "4ch", universe: 3));

        Assert.Equal(5, Occupancy(project, 1).FindFreeAddress(1));
        Assert.Equal(9, Occupancy(project, 2).FindFreeAddress(1));
        Assert.Equal(1, Occupancy(project, 3).FindFreeAddress(19));
        Assert.Equal(1, Occupancy(project, 4).FindFreeAddress(512));
    }

    [Fact]
    public void Build_UnresolvableProfileOrMode_IsNotOccupied()
    {
        var ghost = Par("ghost", 1, "4ch");
        ghost.ProfileId = "missing";
        var badMode = Par("bad mode", 10, "99ch");
        var occupancy = Occupancy(ProjectWith(ghost, badMode));

        Assert.True(occupancy.IsFree(1));
        Assert.True(occupancy.IsFree(10));
        Assert.Null(occupancy.FootprintOf(ghost));
    }

    [Fact]
    public void FindConflicts_FootprintOfOtherUniverse_IsIgnored()
    {
        var project = ProjectWith(Par("a", 1, "8ch"));
        var occupancy = Occupancy(project);

        Assert.Empty(occupancy.FindConflicts([new FixtureFootprint(2, 1, 8)]));
    }

    [Fact]
    public void FindConflicts_PlannedFootprintTouchingEnd512_ReportsFixtureThere()
    {
        var project = ProjectWith(Par("tail", 505, "8ch"));
        var occupancy = Occupancy(project);

        Assert.Single(occupancy.FindConflicts([new FixtureFootprint(1, 512, 1)]));
        Assert.Empty(occupancy.FindConflicts([new FixtureFootprint(1, 504, 1)]));
    }

    [Fact]
    public void FindConflicts_PlannedFootprintCrossing512_StillReportsConflictsAndDoesNotThrow()
    {
        var project = ProjectWith(Par("tail", 510, "4ch"));
        var occupancy = Occupancy(project);

        Assert.Single(occupancy.FindConflicts([new FixtureFootprint(1, 500, 20)]));
        Assert.Empty(occupancy.FindConflicts([new FixtureFootprint(1, 600, 4)]));
    }

    [Fact]
    public void FindConflicts_NoFootprints_ReturnsEmpty()
    {
        var occupancy = Occupancy(ProjectWith(Par("a", 1, "8ch")));

        Assert.Empty(occupancy.FindConflicts([]));
    }

    [Fact]
    public void FindConflicts_FixtureHitByTwoFootprints_ListedOnce()
    {
        var project = ProjectWith(Par("wide", 1, "8ch"));
        var occupancy = Occupancy(project);

        var conflicts = occupancy.FindConflicts([new FixtureFootprint(1, 1, 1), new FixtureFootprint(1, 8, 1)]);

        Assert.Single(conflicts);
    }

    [Fact]
    public void FindConflicts_TwoFixturesOnSameAddresses_BothReportedInPatchOrder()
    {
        var a = Par("a", 1, "4ch");
        var b = Par("b", 1, "4ch");
        var occupancy = Occupancy(ProjectWith(a, b));

        var conflicts = occupancy.FindConflicts([new FixtureFootprint(1, 2, 1)]);

        Assert.Equal([a, b], conflicts);
    }

    [Fact]
    public void FindFreeAddress_RandomPatches_MatchesPatchValidator()
    {
        var random = new Random(20260622);
        for (var round = 0; round < 300; round++)
        {
            var project = ProjectWith();
            var count = random.Next(0, 40);
            for (var i = 0; i < count; i++)
            {
                var address = random.Next(-5, 530);
                var mode = random.Next(3) == 0 ? "8ch" : "4ch";
                var enabled = random.Next(5) != 0;
                var universe = random.Next(1, 3);
                project.Fixtures.Add(Par("f" + i, address, mode, universe, enabled));
            }

            var ignore = project.Fixtures.Count > 0 && random.Next(2) == 0
                ? project.Fixtures[random.Next(project.Fixtures.Count)].Id
                : (Guid?)null;
            var universeToAsk = random.Next(1, 3);
            var occupancy = UniverseOccupancy.Build(project.Fixtures, project, universeToAsk, ignore);
            foreach (var channels in new[] { 1, 4, 8, 17, 64, 200, 512 })
            {
                Assert.Equal(
                    PatchValidator.FindFreeAddress(project.Fixtures, project, universeToAsk, channels, ignore),
                    occupancy.FindFreeAddress(channels));
            }
        }
    }
}
