using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.Engine.Tests;

/// <summary>Fixtures built with the channel layout editor in a non-standard channel order.</summary>
public class CustomLayoutPlaybackTests
{
    // Green, Dimmer, Red, Blue at address 1 → red sits on channel 3.
    private static FixtureProfile GreenDimmerRedBlue() => ChannelLayout.CreateProfile(
        "Acme",
        "Odd PAR",
        [
            new ChannelLayoutRow { Type = ChannelType.Green },
            new ChannelLayoutRow { Type = ChannelType.Dimmer, DefaultValue = 255 },
            new ChannelLayoutRow { Type = ChannelType.Red },
            new ChannelLayoutRow { Type = ChannelType.Blue },
        ]);

    private static (Project Project, Fixture Fixture, DmxEngine Engine) Rig()
    {
        var project = new Project();
        var profile = GreenDimmerRedBlue();
        var fixture = project.AddFixtures(profile, profile.Modes[0].Name, 1, 1, 1, "Odd")[0];
        var engine = new DmxEngine { SnapshotInterval = TimeSpan.Zero };
        return (project, fixture, engine);
    }

    private static byte[] Frame(DmxEngine engine) => engine.GetSnapshot()!.GetUniverse(1).ToArray();

    [Fact]
    public void StartFunction_RedSceneOnCustomLayout_WritesRedOnChannel3()
    {
        var (project, fixture, engine) = Rig();
        var profile = project.FindProfile(fixture.ProfileId)!;
        var redKey = profile.Channels.Single(c => c.Type == ChannelType.Red).Key;
        var scene = new SceneFunction { Name = "Red" };
        scene.SetValue(fixture.Id, redKey, 255);
        project.Functions.Add(scene);
        engine.LoadProject(project);
        engine.Tick(TimeSpan.Zero);

        engine.StartFunction(scene.Id, EngineLayer.Console);
        engine.Tick(TimeSpan.FromSeconds(0.025));

        var expected = new byte[DmxAddress.UniverseSize];
        expected[DmxAddress.ToIndex(2)] = 255; // dimmer at its layout default
        expected[DmxAddress.ToIndex(3)] = 255; // red
        Assert.Equal(expected, Frame(engine));
    }

    [Fact]
    public void Tick_CustomLayoutIdle_OutputsLayoutDefaults()
    {
        var (project, _, engine) = Rig();
        engine.LoadProject(project);

        engine.Tick(TimeSpan.FromSeconds(0.025));

        var expected = new byte[DmxAddress.UniverseSize];
        expected[DmxAddress.ToIndex(2)] = 255;
        Assert.Equal(expected, Frame(engine));
    }

    [Fact]
    public void StartFunction_SceneWithStaleKeyAfterLayoutEdit_IgnoresStaleKeyAndPlaysRest()
    {
        var (project, fixture, engine) = Rig();
        var scene = new SceneFunction { Name = "Look" };
        scene.SetValue(fixture.Id, "blue", 200);
        scene.SetValue(fixture.Id, "red", 100);
        project.Functions.Add(scene);

        // Edit: drop Blue, move Red to channel 1.
        var edited = ChannelLayout.CreateProfile(
            "Acme",
            "Odd PAR",
            [
                new ChannelLayoutRow { Type = ChannelType.Red },
                new ChannelLayoutRow { Type = ChannelType.Green },
                new ChannelLayoutRow { Type = ChannelType.Dimmer, DefaultValue = 255 },
            ],
            project.FindProfile(fixture.ProfileId));
        project.UpdateProfile(edited);

        engine.LoadProject(project);
        engine.Tick(TimeSpan.Zero);
        engine.StartFunction(scene.Id, EngineLayer.Console);
        engine.Tick(TimeSpan.FromSeconds(0.025));

        var expected = new byte[DmxAddress.UniverseSize];
        expected[DmxAddress.ToIndex(1)] = 100; // red followed its meaning to channel 1
        expected[DmxAddress.ToIndex(3)] = 255; // dimmer default
        Assert.Equal(expected, Frame(engine));
    }

    [Fact]
    public void Tick_CustomStrobeChannelStrobeNotAllowed_HoldsDefault()
    {
        var project = new Project();
        var profile = ChannelLayout.CreateProfile(
            "Acme",
            "Strobe PAR",
            [
                new ChannelLayoutRow { Type = ChannelType.Strobe },
                new ChannelLayoutRow { Type = ChannelType.Red },
            ]);
        var fixture = project.AddFixtures(profile, profile.Modes[0].Name, 1, 1, 1, "S")[0];
        var scene = new SceneFunction { Name = "Strobe" };
        scene.SetValue(fixture.Id, "strobe", 200);
        project.Functions.Add(scene);
        var engine = new DmxEngine { SnapshotInterval = TimeSpan.Zero, AllowStrobe = false };
        engine.LoadProject(project);
        engine.Tick(TimeSpan.Zero);

        engine.StartFunction(scene.Id, EngineLayer.Console);
        engine.Tick(TimeSpan.FromSeconds(0.025));

        Assert.Equal(0, engine.GetSnapshot()!.GetValue(1, 1));
    }
}
