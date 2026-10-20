using DmxPro.Core.Patching;

namespace DmxPro.Engine.Tests;

public class ScenePlaybackTests
{
    [Fact]
    public void StartFunction_SceneRedFull_SetsOnlyTheRedAddress()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();

        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        var expected = TestRig.DefaultsFrame();
        expected[DmxAddress.ToIndex(TestRig.Red)] = 255;
        Assert.Equal(expected, rig.Universe());
    }

    [Fact]
    public void StartFunction_ModeWithDimmerFirst_RedResolvedFromProfile()
    {
        // 5ch mode: dimmer, red, green, blue, white — red sits at start + 1, not at the start address.
        var rig = new TestRig(mode: "5ch", startAddress: 1);
        var scene = rig.Scene("Red", ("r", 255));
        rig.Load();

        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        var expected = new byte[DmxAddress.UniverseSize];
        expected[DmxAddress.ToIndex(1)] = 255; // dimmer default
        expected[DmxAddress.ToIndex(2)] = 255; // red
        Assert.Equal(expected, rig.Universe());
    }

    [Fact]
    public void Tick_NothingRunning_OutputsProfileDefaults()
    {
        var rig = new TestRig();
        rig.Load();

        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StartFunction_FadeIn_InterpolatesLinearly()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        scene.FadeInSeconds = 1;
        rig.Load();

        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick(0.5);
        Assert.Equal(128, rig.At(TestRig.Red)); // 127.5 rounds half away from zero

        rig.Tick(0.25);
        Assert.Equal(191, rig.At(TestRig.Red)); // 191.25

        rig.Tick(0.5);
        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void StartFunction_FadeInOnDimmerDefault_CrossFadesFromDefault()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Dim off", ("dim", 0));
        scene.FadeInSeconds = 1;
        rig.Load();

        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick(0.25);

        Assert.Equal(191, rig.At(TestRig.Dimmer)); // 255 × 0.75
    }

    [Fact]
    public void StopFunction_FadeOut_FadesToDefaultThenReleases()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        scene.FadeOutSeconds = 2;
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        rig.Engine.StopFunction(scene.Id, EngineLayer.Console);
        rig.Tick(1);
        Assert.Equal(128, rig.At(TestRig.Red));
        Assert.True(rig.Engine.GetSnapshot()!.ActiveFunctions.Single().IsStopping);

        rig.Tick(1);
        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void SetFunctionIntensity_Half_ScalesIntensityChannelsOnly()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Mix", ("r", 200), ("macro", 100));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        rig.Engine.SetFunctionIntensity(scene.Id, EngineLayer.Console, 0.5);
        rig.Tick();

        Assert.Equal(100, rig.At(TestRig.Red));
        Assert.Equal(100, rig.At(TestRig.Macro));
    }

    [Fact]
    public void StartFunction_UnknownFunction_IsIgnored()
    {
        var rig = new TestRig();
        rig.Load();

        rig.Engine.StartFunction(Guid.NewGuid(), EngineLayer.Console);
        rig.Tick();

        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StartFunction_SceneValueForChannelNotInMode_IsSkipped()
    {
        var rig = new TestRig(mode: "5ch", startAddress: 1);
        var scene = rig.Scene("Strobe", ("strobe", 200), ("g", 50));
        rig.Engine.AllowStrobe = true;
        rig.Load();

        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        var expected = new byte[DmxAddress.UniverseSize];
        expected[DmxAddress.ToIndex(1)] = 255;
        expected[DmxAddress.ToIndex(3)] = 50;
        Assert.Equal(expected, rig.Universe());
    }
}
