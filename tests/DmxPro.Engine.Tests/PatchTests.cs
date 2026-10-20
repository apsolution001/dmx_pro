using DmxPro.Core.Profiles;

namespace DmxPro.Engine.Tests;

public class PatchTests
{
    [Fact]
    public void TryResolveChannel_PatchedFixture_ReturnsUniverseAddressTypeAndDefault()
    {
        var rig = new TestRig();
        rig.Engine.LoadProject(rig.Project);

        Assert.True(rig.Engine.TryResolveChannel(rig.Fixture.Id, "dim", out var channel));

        Assert.Equal(new PatchedChannel(1, TestRig.Dimmer, ChannelType.Dimmer, 255), channel);
        Assert.False(rig.Engine.TryResolveChannel(rig.Fixture.Id, "pan", out _));
    }

    [Fact]
    public void LoadProject_SecondUniverse_OutputsBothUniverses()
    {
        var rig = new TestRig();
        rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 3, 1, "Far");
        rig.Load();

        rig.Tick();

        Assert.Equal([1, 3], rig.Engine.GetSnapshot()!.Universes);
        Assert.Equal(255, rig.At(5, universe: 3)); // dimmer of the 8ch fixture at address 1
    }

    [Fact]
    public void LoadProject_DisabledFixture_IsNotPatched()
    {
        var rig = new TestRig();
        rig.Fixture.Enabled = false;
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();

        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        Assert.All(rig.Universe(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void RefreshPatch_FixtureReaddressed_RunningSceneFollowsFixture()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        rig.Fixture.StartAddress = 100;
        rig.RefreshAndApply();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(255, rig.At(100));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void RefreshPatch_RunningFunctionDeleted_StopsIt()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        rig.Project.Functions.Remove(scene);
        rig.RefreshAndApply();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void RefreshPatch_Overrides_AreKept()
    {
        var rig = new TestRig();
        rig.Load();
        rig.Engine.SetChannelOverride(1, 200, 42);
        rig.Tick();

        rig.RefreshAndApply();

        Assert.Equal(42, rig.At(200));
    }

    [Fact]
    public void LoadProject_WhileFunctionsRun_ReleasesEverything()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Engine.SetChannelOverride(1, 200, 42);
        rig.Tick();

        rig.Load();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void CanPlay_SceneAndUnknownId_ReportsPlayability()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        rig.Engine.LoadProject(rig.Project);

        Assert.True(rig.Engine.CanPlay(scene.Id));
        Assert.False(rig.Engine.CanPlay(Guid.NewGuid()));
    }
}
