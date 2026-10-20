namespace DmxPro.Engine.Tests;

public class SafeFrameTests
{
    [Fact]
    public void CreateSafeFrame_ProfileDefaults_ReturnsDefaultsEvenWhileFunctionsRun()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.StartFunction(red.Id, EngineLayer.Console);
        rig.Tick();

        var frame = rig.Engine.CreateSafeFrame(1, SafeFrameMode.ProfileDefaults);

        Assert.Equal(TestRig.DefaultsFrame(), frame);
    }

    [Fact]
    public void CreateSafeFrame_Blackout_ZeroesIntensityChannels()
    {
        var rig = new TestRig();
        rig.Load();

        var frame = rig.Engine.CreateSafeFrame(1, SafeFrameMode.Blackout);

        Assert.Equal(new byte[512], frame);
    }

    [Fact]
    public void CreateSafeFrame_UnusedUniverse_ReturnsAllZeros()
    {
        var rig = new TestRig();
        rig.Load();

        var frame = rig.Engine.CreateSafeFrame(7, SafeFrameMode.ProfileDefaults);

        Assert.Equal(new byte[512], frame);
    }
}
