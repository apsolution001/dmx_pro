using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Testing;

namespace DmxPro.Core.Tests;

public class PatchHealthTests
{
    private static (Project Project, Fixture Fixture) Create(string mode = "8ch", int start = 1)
    {
        var project = new Project();
        var fixture = project.AddFixtures(TestProfiles.Rgbw(), mode, 1, 1, start, "Par")[0];
        return (project, fixture);
    }

    [Fact]
    public void Analyze_CleanUnverifiedFixture_OnlyUnverified()
    {
        var (project, fixture) = Create();

        var health = PatchHealth.Analyze(project);
        var entry = health.Find(fixture.Id)!;

        Assert.True(entry.IsUnverified);
        Assert.False(entry.HasOverlap);
        Assert.False(entry.ExceedsUniverse);
        Assert.Null(entry.ProblemText);
        Assert.True(entry.HasStrobeChannel);
        Assert.True(health.AnyStrobeChannel);
        Assert.Equal(1, health.UnverifiedCount);
    }

    [Fact]
    public void Analyze_VerifiedProfile_NotUnverified()
    {
        var (project, fixture) = Create();
        project.FindProfile(fixture.ProfileId)!.Verified = true;

        Assert.Equal(0, PatchHealth.Analyze(project).UnverifiedCount);
    }

    [Fact]
    public void Analyze_MissingProfile_ReportsProblem()
    {
        var (project, fixture) = Create();
        fixture.ProfileId = "nope";

        var health = PatchHealth.Analyze(project);
        var entry = health.Find(fixture.Id)!;

        Assert.True(entry.IsMissingProfile);
        Assert.NotNull(entry.ProblemText);
        Assert.Equal(1, health.MissingProfileOrModeCount);
        Assert.False(health.AnyStrobeChannel);
    }

    [Fact]
    public void Analyze_MissingMode_ReportsProblem()
    {
        var (project, fixture) = Create();
        fixture.ModeName = "99ch";

        var entry = PatchHealth.Analyze(project).Find(fixture.Id)!;

        Assert.True(entry.IsMissingMode);
        Assert.Contains("99ch", entry.ProblemText);
    }

    [Fact]
    public void Analyze_FootprintPast512_ExceedsUniverse()
    {
        var (project, fixture) = Create();
        fixture.StartAddress = 510;

        var health = PatchHealth.Analyze(project);

        Assert.True(health.Find(fixture.Id)!.ExceedsUniverse);
        Assert.Equal(1, health.OutOfRangeCount);
    }

    [Fact]
    public void Analyze_OverlappingEnabledFixtures_BothFlagged()
    {
        var (project, fixture) = Create();
        var other = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "Twin")[0];
        other.StartAddress = 5;

        var health = PatchHealth.Analyze(project);

        Assert.True(health.Find(fixture.Id)!.HasOverlap);
        Assert.Equal(["Twin"], health.Find(fixture.Id)!.OverlapsWith);
        Assert.Equal(2, health.OverlapCount);
    }

    [Fact]
    public void Analyze_OverlapWithDisabledFixture_NotFlagged()
    {
        var (project, fixture) = Create();
        var other = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 1, "Twin")[0];
        other.StartAddress = 5;
        other.Enabled = false;

        var health = PatchHealth.Analyze(project);

        Assert.False(health.Find(fixture.Id)!.HasOverlap);
        Assert.Equal(1, health.DisabledCount);
        Assert.NotNull(health.Find(other.Id)!.ProblemText);
    }

    [Fact]
    public void Analyze_SameAddressOtherUniverse_NoOverlap()
    {
        var (project, _) = Create();
        var other = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 2, 1, "Far")[0];
        other.StartAddress = 1;

        Assert.Equal(0, PatchHealth.Analyze(project).OverlapCount);
    }

    [Fact]
    public void Analyze_InvalidUniverse_ReportsProblem()
    {
        var (project, fixture) = Create();
        fixture.Universe = 0;

        var health = PatchHealth.Analyze(project);
        var entry = health.Find(fixture.Id)!;

        Assert.True(entry.HasInvalidUniverse);
        Assert.Contains("Universe 0", entry.ProblemText);
        Assert.Equal(1, health.OutOfRangeCount);
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        var (project, _) = Create();

        Assert.Null(PatchHealth.Analyze(project).Find(Guid.NewGuid()));
    }

    [Fact]
    public void Analyze_OverlapsInSeveralUniverses_NamesOnlySameUniverseInPatchOrder()
    {
        var project = new Project();
        var profile = TestProfiles.Rgbw();
        var a = project.AddFixtures(profile, "8ch", 1, 1, 1, "A")[0];
        var b = project.AddFixtures(profile, "8ch", 1, 2, 1, "B")[0];
        var c = project.AddFixtures(profile, "8ch", 1, 1, 5, "C")[0];
        var d = project.AddFixtures(profile, "8ch", 1, 1, 3, "D")[0];
        var e = project.AddFixtures(profile, "8ch", 1, 2, 8, "E")[0];

        var health = PatchHealth.Analyze(project);

        Assert.Equal(["C", "D"], health.Find(a.Id)!.OverlapsWith);
        Assert.Equal(["E"], health.Find(b.Id)!.OverlapsWith);
        Assert.Equal(["A", "D"], health.Find(c.Id)!.OverlapsWith);
        Assert.Equal(["A", "C"], health.Find(d.Id)!.OverlapsWith);
        Assert.Equal(["B"], health.Find(e.Id)!.OverlapsWith);
    }
}
