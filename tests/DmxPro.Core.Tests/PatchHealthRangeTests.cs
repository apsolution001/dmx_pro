using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Testing;

namespace DmxPro.Core.Tests;

/// <summary>Overlap detection in <see cref="PatchHealth"/> ignores fixtures that cannot be sent, like the validator.</summary>
public class PatchHealthRangeTests
{
    [Fact]
    public void Analyze_StartAddressZeroSharingChannels_NotReportedAsOverlapping()
    {
        var project = new Project();
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2, 1, 1, "Par");
        fixtures[0].StartAddress = 0; // invalid: 0–3

        var health = PatchHealth.Analyze(project);

        Assert.Equal(0, health.OverlapCount);
        Assert.Equal(0, PatchValidator.Validate(project.Fixtures, project).Count(i => i.Code == "ADDRESS_OVERLAP"));
    }

    [Fact]
    public void Analyze_ValidOverlapNextToInvalidFixture_StillReported()
    {
        var project = new Project();
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 3, 1, 1, "Par");
        fixtures[1].StartAddress = 3; // overlaps fixture 0 (1–4)
        fixtures[2].Universe = 0;
        fixtures[2].StartAddress = 1;

        var health = PatchHealth.Analyze(project);

        Assert.Equal(2, health.OverlapCount);
        Assert.False(health.Find(fixtures[2].Id)!.HasOverlap);
        Assert.Equal(1, health.OutOfRangeCount);
    }
}
