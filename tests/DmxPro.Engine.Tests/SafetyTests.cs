using DmxPro.Core.Profiles;

namespace DmxPro.Engine.Tests;

public class SafetyTests
{
    [Fact]
    public void GrandMaster_Half_ScalesOnlyIntensityChannels()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Mix", ("r", 200), ("macro", 100), ("speed", 60));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);

        rig.Engine.GrandMaster = 0.5;
        rig.Tick();

        Assert.Equal(100, rig.At(TestRig.Red));
        Assert.Equal(128, rig.At(TestRig.Dimmer)); // default 255 is intensity too
        Assert.Equal(100, rig.At(TestRig.Macro));
        Assert.Equal(60, rig.At(TestRig.Speed));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    [InlineData(double.NaN, 0)]
    public void GrandMaster_OutOfRange_IsClamped(double value, double expected)
    {
        var engine = new DmxEngine { GrandMaster = value };

        Assert.Equal(expected, engine.GrandMaster);
    }

    [Fact]
    public void Blackout_On_ZerosIntensityKeepsOthersAndKeepsFunctionsRunning()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Mix", ("r", 200), ("macro", 100));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Engine.SetChannelOverride(1, TestRig.Green, 90);

        rig.Engine.Blackout = true;
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green)); // overrides are blacked out too
        Assert.Equal(0, rig.At(TestRig.Dimmer));
        Assert.Equal(100, rig.At(TestRig.Macro));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.True(rig.Engine.GetSnapshot()!.Blackout);

        rig.Engine.Blackout = false;
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Red));
    }

    [Fact]
    public void ReleaseAll_EverythingRunning_ReturnsToDefaults()
    {
        var rig = new TestRig();
        var show = rig.Scene("Show", ("r", 255));
        var console = rig.Scene("Console", ("g", 255), ("macro", 70));
        console.FadeOutSeconds = 10; // release ignores fades
        var preview = rig.Scene("Preview", ("b", 255));
        var timeline = rig.Project.CreateShow("Timeline");
        rig.Project.PlaceFunctionInShow(timeline.Id, timeline.Tracks[0].Id, show.Id, 0, 60);
        rig.Load();
        rig.Engine.StartShow(timeline.Id);
        rig.Engine.StartFunction(show.Id, EngineLayer.Show);
        rig.Engine.StartFunction(console.Id, EngineLayer.Console);
        rig.Engine.StartPreview(preview.Id);
        rig.Engine.SetChannelOverride(1, 300, 12);
        rig.Tick();

        rig.Engine.ReleaseAll();
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        var snapshot = rig.Engine.GetSnapshot()!;
        Assert.Empty(snapshot.ActiveFunctions);
        Assert.Empty(snapshot.Shows);
    }

    [Fact]
    public void AllowStrobe_False_ForcesSafeStrobeValue()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Strobe", ("strobe", 200));
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Engine.SetChannelOverride(1, TestRig.Strobe, 250);

        rig.Tick();
        Assert.False(rig.Engine.AllowStrobe); // safe by default
        Assert.Equal(0, rig.At(TestRig.Strobe));

        rig.Engine.ClearOverrides();
        rig.Engine.AllowStrobe = true;
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Strobe));
    }

    [Fact]
    public void AllowStrobe_FalseAndDefaultOutsideSafeRange_UsesFirstSafeRangeStart()
    {
        var rig = new TestRig();
        var strobe = rig.Project.Profiles[0].FindChannel("strobe")!;
        strobe.Default = 200;
        strobe.Capabilities =
        [
            new ChannelCapability { Min = 0, Max = 4, Label = "Blackout" },
            new ChannelCapability { Min = 5, Max = 9, Label = "Open", Safe = true },
            new ChannelCapability { Min = 10, Max = 255, Label = "Strobe" },
        ];
        rig.Load();

        rig.Tick();

        Assert.Equal(5, rig.At(TestRig.Strobe));
    }

    [Theory]
    [InlineData(300f, 255)]
    [InlineData(255f, 255)]
    [InlineData(254.4f, 254)]
    [InlineData(127.5f, 128)]
    [InlineData(0.4f, 0)]
    [InlineData(-5f, 0)]
    [InlineData(float.NaN, 0)]
    [InlineData(float.PositiveInfinity, 255)]
    public void ToByte_AnyValue_ClampedAndRounded(float value, byte expected)
    {
        Assert.Equal(expected, DmxEngine.ToByte(value));
    }

    [Fact]
    public void Tick_HtpOfTwoLayersAtFullDimmer_NeverExceeds255()
    {
        var rig = new TestRig();
        var a = rig.Scene("A", ("dim", 255), ("r", 255));
        var b = rig.Scene("B", ("dim", 255), ("r", 255));
        b.FadeInSeconds = 1;
        rig.Load();
        rig.Engine.StartFunction(a.Id, EngineLayer.Show);
        rig.Engine.StartFunction(b.Id, EngineLayer.Console);

        rig.Tick(0.5);

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(255, rig.At(TestRig.Dimmer));
    }
}
