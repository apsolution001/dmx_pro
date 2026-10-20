using DmxPro.Core.Functions;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class ProjectUpdateProfileTests
{
    private static List<ChannelLayoutRow> Rows(params ChannelType[] types) =>
        types.Select(t => new ChannelLayoutRow { Type = t, DefaultValue = ChannelLayout.DefaultValueFor(t) }).ToList();

    private static (Project Project, FixtureProfile Profile) TwoFixtures()
    {
        var project = new Project();
        var profile = ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White));
        project.AddFixtures(profile, "4ch", 2, 1, 1, "PAR"); // 1–4, 5–8
        return (project, project.FindProfile(profile.Id)!);
    }

    [Fact]
    public void UpdateProfile_MoreChannels_FixturesMoveToNewModeAndOverlapReported()
    {
        var (project, profile) = TwoFixtures();
        var other = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 1, 1, 9, "Other")[0]; // 9–12
        var edited = ChannelLayout.CreateProfile(
            "X",
            "PAR",
            Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White, ChannelType.Strobe),
            profile);

        var result = project.UpdateProfile(edited);

        Assert.Equal(project.Fixtures.Take(2).Select(f => f.Id), result.AffectedFixtureIds);
        Assert.All(project.Fixtures.Take(2), f => Assert.Equal("6ch", f.ModeName));
        Assert.Equal("4ch", other.ModeName);
        Assert.Equal(2, project.FindProfile(profile.Id)!.Revision);

        // 1–6 overlaps 5–10, and 5–10 overlaps the other fixture at 9–12.
        Assert.Equal(2, result.PatchIssues.Count(i => i.Code == "ADDRESS_OVERLAP"));
        Assert.All(result.PatchIssues, i => Assert.Contains(i.EntityId!.Value, result.AffectedFixtureIds));
        Assert.DoesNotContain(result.PatchIssues, i => i.Code == "PROFILE_UNVERIFIED");
    }

    [Fact]
    public void UpdateProfile_FewerChannels_FixturesUpdatedNoPatchIssues()
    {
        var (project, profile) = TwoFixtures();

        var result = project.UpdateProfile(ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue), profile));

        Assert.Equal(2, result.AffectedFixtureIds.Count);
        Assert.All(project.Fixtures, f => Assert.Equal("3ch", f.ModeName));
        Assert.Empty(result.PatchIssues);
        Assert.Single(project.Profiles);
    }

    [Fact]
    public void UpdateProfile_FootprintPast512_Reported()
    {
        var project = new Project();
        var profile = ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue));
        var fixture = project.AddFixtures(profile, "3ch", 1, 1, 510, "End")[0];

        var result = project.UpdateProfile(
            ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue), profile));

        var issue = Assert.Single(result.PatchIssues);
        Assert.Equal("FOOTPRINT_EXCEEDS_UNIVERSE", issue.Code);
        Assert.Equal(fixture.Id, issue.EntityId);
    }

    [Fact]
    public void UpdateProfile_SceneKeyRemoved_ReportedAndValueKept()
    {
        var (project, profile) = TwoFixtures();
        var scene = new SceneFunction { Name = "White" };
        scene.SetValue(project.Fixtures[0].Id, "white", 255);
        project.Functions.Add(scene);

        var result = project.UpdateProfile(ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red, ChannelType.Green, ChannelType.Blue), profile));

        Assert.Contains(result.FunctionIssues, i => i.Code == "SCENE_UNKNOWN_CHANNEL" && i.EntityId == scene.Id);
        Assert.Equal((byte)255, scene.GetValue(project.Fixtures[0].Id, "white"));
    }

    [Fact]
    public void UpdateProfile_SameChannelCountReordered_ModeKeptLayoutChanged()
    {
        var (project, profile) = TwoFixtures();

        var result = project.UpdateProfile(
            ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.White, ChannelType.Blue, ChannelType.Green, ChannelType.Red), profile));

        Assert.All(project.Fixtures, f => Assert.Equal("4ch", f.ModeName));
        Assert.Empty(result.PatchIssues);
        Assert.Equal(["white", "blue", "green", "red"], project.FindProfile(profile.Id)!.Modes[0].Channels);
    }

    [Fact]
    public void UpdateProfile_StoresCopy_LaterChangesToArgumentDoNotLeak()
    {
        var (project, profile) = TwoFixtures();
        var edited = ChannelLayout.CreateProfile("X", "PAR v2", Rows(ChannelType.Red), profile);

        project.UpdateProfile(edited);
        edited.Model = "changed";

        Assert.Equal("PAR v2", project.FindProfile(profile.Id)!.Model);
    }

    [Fact]
    public void UpdateProfile_UnusedProfile_EmbeddedWithNoAffectedFixtures()
    {
        var project = new Project();

        var result = project.UpdateProfile(ChannelLayout.CreateProfile("X", "PAR", Rows(ChannelType.Red)));

        Assert.Empty(result.AffectedFixtureIds);
        Assert.Single(project.Profiles);
    }

    [Fact]
    public void Serialize_ProjectWithCustomProfile_RoundTripsUserDefined()
    {
        var (project, profile) = TwoFixtures();
        var serializer = new ProjectSerializer();

        var back = serializer.Deserialize(serializer.Serialize(project));

        Assert.True(ChannelLayout.IsCustom(back.Project.FindProfile(profile.Id)!));
    }
}
