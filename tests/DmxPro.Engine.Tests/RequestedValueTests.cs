namespace DmxPro.Engine.Tests;

/// <summary>
/// The requested value is what the functions and overrides ask for, before Grand Master, Blackout and strobe safety;
/// copying a look must use it, or a paste applies the masters twice.
/// </summary>
public class RequestedValueTests
{
    [Fact]
    public void GetRequestedValue_GrandMasterHalf_IsUnscaledWhileOutputIsScaled()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255), ("macro", 100));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);

        rig.Engine.GrandMaster = 0.5;
        rig.Tick();

        var snapshot = rig.Engine.GetSnapshot()!;
        Assert.Equal(255, snapshot.GetRequestedValue(1, TestRig.Red));
        Assert.Equal(255, snapshot.GetRequestedValue(1, TestRig.Dimmer));
        Assert.Equal(100, snapshot.GetRequestedValue(1, TestRig.Macro));
        Assert.Equal(128, snapshot.GetValue(1, TestRig.Red));
        Assert.Equal(128, snapshot.GetValue(1, TestRig.Dimmer));
    }

    [Fact]
    public void GetRequestedValue_BlackoutOn_KeepsValuesWhileOutputIsDark()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 200));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Engine.SetChannelOverride(1, TestRig.Green, 90);

        rig.Engine.Blackout = true;
        rig.Tick();

        var snapshot = rig.Engine.GetSnapshot()!;
        Assert.Equal(200, snapshot.GetRequestedValue(1, TestRig.Red));
        Assert.Equal(90, snapshot.GetRequestedValue(1, TestRig.Green));
        Assert.Equal(255, snapshot.GetRequestedValue(1, TestRig.Dimmer));
        Assert.Equal(0, snapshot.GetValue(1, TestRig.Red));
        Assert.Equal(0, snapshot.GetValue(1, TestRig.Green));
        Assert.Equal(0, snapshot.GetValue(1, TestRig.Dimmer));
    }

    [Fact]
    public void GetRequestedValue_StrobeNotAllowed_KeepsProgrammedStrobeWhileOutputIsSafe()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Strobe", ("strobe", 200));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);

        rig.Tick();

        var snapshot = rig.Engine.GetSnapshot()!;
        Assert.False(rig.Engine.AllowStrobe);
        Assert.Equal(200, snapshot.GetRequestedValue(1, TestRig.Strobe));
        Assert.Equal(0, snapshot.GetValue(1, TestRig.Strobe));
    }

    [Fact]
    public void GetRequestedValue_Idle_EqualsProfileDefaults()
    {
        var rig = new TestRig();
        rig.Load();
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Engine.GetSnapshot()!.GetRequestedUniverse(1).ToArray());
    }

    [Fact]
    public void GetRequestedValue_NoMastersApplied_EqualsOutput()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Mix", ("r", 37), ("g", 128), ("w", 255), ("speed", 9));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();

        var snapshot = rig.Engine.GetSnapshot()!;
        Assert.Equal(snapshot.GetUniverse(1).ToArray(), snapshot.GetRequestedUniverse(1).ToArray());
    }

    [Fact]
    public void GetRequestedUniverse_UnknownUniverse_IsEmptyAndValueZero()
    {
        var rig = new TestRig();
        rig.Load();
        rig.Tick();

        var snapshot = rig.Engine.GetSnapshot()!;
        Assert.True(snapshot.GetRequestedUniverse(7).IsEmpty);
        Assert.Equal(0, snapshot.GetRequestedValue(7, 1));
    }

    [Fact]
    public void Tick_WithMastersApplied_AllocatesNothing()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Mix", ("r", 255), ("strobe", 200));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Engine.GrandMaster = 0.5;
        rig.Engine.Blackout = true;
        Assert.Equal(0, AllocationProbe.Measure(_ => rig.Tick()));
    }
}
