using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>Adding lights of a profile that the project already embeds (safety rule 7: Verified never flips silently).</summary>
public class ProjectEmbedProfileTests
{
    [Fact]
    public void AddFixtures_ProfileAlreadyEmbeddedAndVerified_KeepsVerifiedFlag()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "Par");
        project.FindProfile("test-rgbw")!.Verified = true; // the owner verified it in this project

        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 10, "Par"); // library copy: not verified

        Assert.True(project.FindProfile("test-rgbw")!.Verified);
        Assert.Single(project.Profiles);
    }

    [Fact]
    public void AddFixtures_ProfileAlreadyEmbeddedAndUnverified_KeepsUnverified()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "Par");
        var library = TestProfiles.Rgbw();
        library.Verified = true;

        project.AddFixtures(library, "4ch", 1, 1, 10, "Par");

        Assert.False(project.FindProfile("test-rgbw")!.Verified);
    }

    [Fact]
    public void AddFixtures_ProfileAlreadyEmbedded_KeepsEmbeddedInstance()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "Par");
        var embedded = project.FindProfile("test-rgbw");

        project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 10, "Par");

        Assert.Same(embedded, project.FindProfile("test-rgbw"));
        Assert.Equal(2, project.Fixtures.Count);
    }

    [Fact]
    public void AddFixtures_LibraryCopyIsNewerRevision_ReplacesWithLibraryVerifiedFlag()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "Par");
        project.FindProfile("test-rgbw")!.Verified = true; // verified the OLD layout
        var newer = TestProfiles.Rgbw();
        newer.Revision = 2;
        newer.Verified = false;

        project.AddFixtures(newer, "4ch", 1, 1, 10, "Par");

        var embedded = project.FindProfile("test-rgbw")!;
        Assert.Equal(2, embedded.Revision);
        Assert.False(embedded.Verified); // the changed layout was never checked: never presented as confirmed
    }

    [Fact]
    public void AddFixtures_EmbeddedIsNewerRevision_KeepsEmbeddedCopy()
    {
        var project = new Project();
        var edited = TestProfiles.Rgbw();
        edited.Revision = 3;
        edited.Verified = true;
        project.AddFixtures(edited, "4ch", 1, 1, 1, "Par");

        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 10, "Par");

        var embedded = project.FindProfile("test-rgbw")!;
        Assert.Equal(3, embedded.Revision);
        Assert.True(embedded.Verified);
    }

    [Fact]
    public void AddFixtures_EmbeddedLacksRequestedMode_EmbedsLibraryCopy()
    {
        var project = new Project();
        var reduced = TestProfiles.Rgbw();
        reduced.Modes.RemoveAt(1); // only "4ch"
        project.AddFixtures(reduced, "4ch", 1, 1, 1, "Par");

        var added = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 1, 10, "Par");

        Assert.NotNull(project.FindProfile("test-rgbw")!.FindMode("8ch"));
        Assert.Equal("8ch", added[0].ModeName);
    }

    [Fact]
    public void AddFixtures_ProfileIdDiffersInCase_UsesEmbeddedId()
    {
        var project = new Project();
        project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 1, "Par");
        var upper = TestProfiles.Rgbw();
        upper.Id = "TEST-RGBW";

        var added = project.AddFixtures(upper, "4ch", 1, 1, 10, "Par");

        Assert.Equal("test-rgbw", added[0].ProfileId);
        Assert.Single(project.Profiles);
    }
}
