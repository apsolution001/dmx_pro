using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class UniverseOccupancyTests
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

    [Fact]
    public void FindFreeAddress_GapBetweenFixtures_ReturnsFirstGapThatFits()
    {
        var project = ProjectWith(Par("A", 1), Par("B", 13));
        var occupancy = UniverseOccupancy.Build(project.Fixtures, project, 1);

        Assert.Equal(9, occupancy.FindFreeAddress(4));
        Assert.Equal(21, occupancy.FindFreeAddress(8));
        Assert.Null(occupancy.FindFreeAddress(510));
    }

    [Fact]
    public void Build_DisabledOtherUniverseAndIgnoredFixtures_LeftOut()
    {
        var ignored = Par("C", 17);
        var project = ProjectWith(Par("A", 1, enabled: false), Par("B", 9, universe: 2), ignored);

        var occupancy = UniverseOccupancy.Build(project.Fixtures, project, 1, ignored.Id);

        Assert.True(occupancy.IsFree(1));
        Assert.True(occupancy.IsFree(9));
        Assert.True(occupancy.IsFree(17));
        Assert.Equal(1, occupancy.FindFreeAddress(512));
    }

    [Fact]
    public void FindConflicts_PlanOverlapsTwo_ReturnsThemInPatchOrder()
    {
        var b = Par("B", 20);
        var a = Par("A", 1);
        var project = ProjectWith(b, Par("Far", 100), a);
        var occupancy = UniverseOccupancy.Build(project.Fixtures, project, 1);

        var conflicts = occupancy.FindConflicts([new FixtureFootprint(1, 5, 8), new FixtureFootprint(1, 21, 2)]);

        Assert.Equal([b, a], conflicts);
        Assert.Equal(new FixtureFootprint(1, 20, 8), occupancy.FootprintOf(b));
    }

    [Fact]
    public void FindConflicts_GapBetweenPlannedFixtures_IsNotAConflict()
    {
        // The plan skips 9–10 (gap 2); a fixture sitting only in the gap does not conflict.
        var project = ProjectWith(Par("Gap", 9, mode: "4ch"));
        var occupancy = UniverseOccupancy.Build(project.Fixtures, project, 1);

        var plan = PatchPlan.Create(1, 1, 8, 2, 4, occupancy);

        Assert.True(plan.IsValid);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Create_WithOccupancy_MatchesFixtureListOverload(int seed)
    {
        var random = new Random(seed);
        var project = ProjectWith();
        for (var i = 0; i < 120; i++)
        {
            project.Fixtures.Add(Par($"F{i}", random.Next(-3, 515), random.Next(2) == 0 ? "4ch" : "8ch", random.Next(1, 3), random.Next(5) != 0));
        }

        for (var trial = 0; trial < 200; trial++)
        {
            var universe = random.Next(1, 3);
            var start = random.Next(0, 514);
            var channels = random.Next(0, 12);
            var quantity = random.Next(0, 6);
            var gap = random.Next(-1, 4);
            var occupancy = UniverseOccupancy.Build(project.Fixtures, project, universe);

            var expected = PatchPlan.Create(universe, start, channels, quantity, gap, project.Fixtures, project);
            var actual = PatchPlan.Create(universe, start, channels, quantity, gap, occupancy);

            Assert.Equal(expected.Problem, actual.Problem);
            Assert.Equal(expected.Footprints, actual.Footprints);
            Assert.Equal(expected.Conflicts, actual.Conflicts);
            Assert.Equal(
                PatchValidator.FindFreeAddress(project.Fixtures, project, universe, Math.Max(1, channels * Math.Max(quantity, 1))),
                occupancy.FindFreeAddress(Math.Max(1, channels * Math.Max(quantity, 1))));
        }
    }

    [Fact]
    public void Create_OccupancyOfOtherUniverse_Throws()
    {
        var project = ProjectWith();
        var occupancy = UniverseOccupancy.Build(project.Fixtures, project, 2);

        Assert.Throws<ArgumentException>(() => PatchPlan.Create(1, 1, 8, 1, 0, occupancy));
    }
}
