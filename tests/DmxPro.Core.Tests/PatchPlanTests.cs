using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class PatchPlanTests
{
    private static Project ProjectWith(params Fixture[] fixtures)
    {
        var project = new Project();
        project.EmbedProfile(TestProfiles.Rgbw());
        project.Fixtures.AddRange(fixtures);
        return project;
    }

    private static Fixture Par(string name, int address, int universe = 1) =>
        new() { Name = name, ProfileId = "test-rgbw", ModeName = "8ch", StartAddress = address, Universe = universe };

    private static PatchPlan Plan(Project project, int start, int channels = 8, int quantity = 1, int gap = 0, int universe = 1) =>
        PatchPlan.Create(universe, start, channels, quantity, gap, project.Fixtures, project);

    [Fact]
    public void Create_ThreeFixturesNoGap_ListsConsecutiveRanges()
    {
        var plan = Plan(ProjectWith(), 1, quantity: 3);

        Assert.True(plan.IsValid);
        Assert.Equal([1, 9, 17], plan.Footprints.Select(f => f.StartAddress));
        Assert.Equal("Will use Universe 1, addresses 1–8, 9–16, 17–24.", plan.Describe());
    }

    [Fact]
    public void Create_WithGap_SkipsGapChannels()
    {
        var plan = Plan(ProjectWith(), 1, quantity: 2, gap: 2);

        Assert.Equal("1–8, 11–18", plan.DescribeRanges());
        Assert.Equal(18, plan.TotalChannels);
        Assert.Equal(18, plan.EndAddress);
    }

    [Fact]
    public void DescribeRanges_ManyFixtures_ShowsFirstAndLast()
    {
        var plan = Plan(ProjectWith(), 1, quantity: 12);

        Assert.Equal("1–8 … 89–96", plan.DescribeRanges());
    }

    [Fact]
    public void Describe_SingleChannelFixture_ShowsSingleAddress()
    {
        var plan = Plan(ProjectWith(), 5, channels: 1);

        Assert.Equal("Will use Universe 1, address 5.", plan.Describe());
    }

    [Theory]
    [InlineData(505, true)]
    [InlineData(506, false)]
    public void Create_BlockAtUniverseEnd_FitsOnlyUpTo512(int start, bool valid)
    {
        var plan = Plan(ProjectWith(), start);

        Assert.Equal(valid, plan.IsValid);
        if (!valid)
        {
            Assert.Contains("Start at 505 or lower", plan.Problem);
        }
    }

    [Fact]
    public void Create_MoreChannelsThanUniverse_IsInvalid()
    {
        var plan = Plan(ProjectWith(), 1, quantity: 65);

        Assert.False(plan.IsValid);
        Assert.Contains("520 channels", plan.Problem);
        Assert.Empty(plan.Footprints);
    }

    [Fact]
    public void Create_OverlapsExistingFixture_ReportsConflict()
    {
        var project = ProjectWith(Par("Front", 9));

        var plan = Plan(project, 1, quantity: 2);

        Assert.False(plan.IsValid);
        Assert.Equal("Front", Assert.Single(plan.Conflicts).Name);
        Assert.StartsWith("Overlaps \"Front\" (U1 9–16)", plan.Problem);
    }

    [Fact]
    public void Create_GapLeavesRoomForExistingFixture_NoConflict()
    {
        var project = ProjectWith(Par("Middle", 9));

        var plan = Plan(project, 1, quantity: 2, gap: 8);

        Assert.True(plan.IsValid);
        Assert.Equal("1–8, 17–24", plan.DescribeRanges());
    }

    [Fact]
    public void Create_ExistingFixtureInOtherUniverseOrDisabled_NoConflict()
    {
        var disabled = Par("Off", 1);
        disabled.Enabled = false;
        var project = ProjectWith(Par("Other", 1, universe: 2), disabled);

        Assert.True(Plan(project, 1).IsValid);
    }

    [Theory]
    [InlineData(0, 1, 1, 0, "Pick a fixture profile and mode.")]
    [InlineData(8, 0, 1, 0, "Address must be 1–512.")]
    [InlineData(8, 1, 0, 0, "Quantity must be 1–512.")]
    [InlineData(8, 1, 1, -1, "Address gap cannot be negative.")]
    public void Create_InvalidInputs_ReportsProblem(int channels, int start, int quantity, int gap, string problem)
    {
        var plan = Plan(ProjectWith(), start, channels, quantity, gap);

        Assert.Equal(problem, plan.Problem);
        Assert.Equal(string.Empty, plan.Describe());
    }

    [Fact]
    public void Create_InvalidUniverse_ReportsProblem()
    {
        var plan = Plan(ProjectWith(), 1, universe: 0);

        Assert.StartsWith("Universe must be", plan.Problem);
    }
}
