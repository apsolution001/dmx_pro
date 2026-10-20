namespace DmxPro.Engine.Tests;

public class LayerMergeTests
{
    [Fact]
    public void Merge_TwoScenesSameLayerIntensity_HighestWins()
    {
        var rig = new TestRig();
        var low = rig.Scene("Low", ("r", 100));
        var high = rig.Scene("High", ("r", 200));
        rig.Load();

        rig.Engine.StartFunction(high.Id, EngineLayer.Console);
        rig.Tick();
        rig.Engine.StartFunction(low.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Red)); // HTP: started later, still loses

        rig.Engine.StopFunction(high.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(100, rig.At(TestRig.Red));
    }

    [Fact]
    public void Merge_TwoScenesSameLayerNonIntensity_LatestStartWins()
    {
        var rig = new TestRig();
        var a = rig.Scene("A", ("macro", 50));
        var b = rig.Scene("B", ("macro", 150));
        rig.Load();

        rig.Engine.StartFunction(a.Id, EngineLayer.Console);
        rig.Tick();
        rig.Engine.StartFunction(b.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(150, rig.At(TestRig.Macro));

        rig.Engine.StartFunction(a.Id, EngineLayer.Console); // re-trigger makes it the latest
        rig.Tick();
        Assert.Equal(50, rig.At(TestRig.Macro));

        rig.Engine.StopFunction(a.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(150, rig.At(TestRig.Macro));
    }

    [Fact]
    public void Merge_StartedInSameTick_LaterCommandWinsLtp()
    {
        var rig = new TestRig();
        var a = rig.Scene("A", ("macro", 50));
        var b = rig.Scene("B", ("macro", 150));
        rig.Load();

        rig.Engine.StartFunction(b.Id, EngineLayer.Console);
        rig.Engine.StartFunction(a.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(50, rig.At(TestRig.Macro));
    }

    [Fact]
    public void Merge_ConsoleOverShow_ConsoleWinsOnTouchedChannelsAndReleaseRestoresShow()
    {
        var rig = new TestRig();
        var show = rig.Scene("Show look", ("r", 255), ("g", 255), ("macro", 20));
        var console = rig.Scene("Console look", ("r", 0), ("b", 100), ("macro", 80));
        rig.Load();

        rig.Engine.StartFunction(show.Id, EngineLayer.Show);
        rig.Tick();
        rig.Engine.StartFunction(console.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red)); // console wins even with a lower intensity value
        Assert.Equal(255, rig.At(TestRig.Green)); // untouched by console → show
        Assert.Equal(100, rig.At(TestRig.Blue));
        Assert.Equal(80, rig.At(TestRig.Macro));

        rig.Engine.StopFunction(console.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(255, rig.At(TestRig.Green));
        Assert.Equal(0, rig.At(TestRig.Blue));
        Assert.Equal(20, rig.At(TestRig.Macro));
    }

    [Fact]
    public void Merge_ShowStartedAfterConsole_ConsoleStillWins()
    {
        var rig = new TestRig();
        var show = rig.Scene("Show", ("macro", 20));
        var console = rig.Scene("Console", ("macro", 80));
        rig.Load();

        rig.Engine.StartFunction(console.Id, EngineLayer.Console);
        rig.Tick();
        rig.Engine.StartFunction(show.Id, EngineLayer.Show);
        rig.Tick();

        Assert.Equal(80, rig.At(TestRig.Macro));
    }

    [Fact]
    public void Merge_ConsoleFadingIn_CrossFadesFromShow()
    {
        var rig = new TestRig();
        var show = rig.Scene("Show", ("r", 200));
        var console = rig.Scene("Console", ("r", 0));
        console.FadeInSeconds = 1;
        rig.Load();
        rig.Engine.StartFunction(show.Id, EngineLayer.Show);
        rig.Tick();

        rig.Engine.StartFunction(console.Id, EngineLayer.Console);
        rig.Tick(0.25);

        Assert.Equal(150, rig.At(TestRig.Red)); // 200 × 0.75
    }

    [Fact]
    public void StartPreview_SecondPreview_ReplacesFirstImmediately()
    {
        var rig = new TestRig();
        var a = rig.Scene("A", ("r", 255));
        a.FadeOutSeconds = 5; // replacement ignores the fade-out
        var b = rig.Scene("B", ("g", 255));
        rig.Load();

        rig.Engine.StartPreview(a.Id);
        rig.Tick();
        rig.Engine.StartPreview(b.Id);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(255, rig.At(TestRig.Green));
        var active = Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.Equal(b.Id, active.FunctionId);
        Assert.Equal(EngineLayer.Preview, active.Layer);
    }

    [Fact]
    public void StartPreview_OverConsole_PreviewWinsAndStopPreviewRestores()
    {
        var rig = new TestRig();
        var console = rig.Scene("Console", ("r", 200), ("g", 40));
        var preview = rig.Scene("Preview", ("r", 10));
        rig.Load();
        rig.Engine.StartFunction(console.Id, EngineLayer.Console);
        rig.Tick();

        rig.Engine.StartPreview(preview.Id);
        rig.Tick();
        Assert.Equal(10, rig.At(TestRig.Red));
        Assert.Equal(40, rig.At(TestRig.Green));
        Assert.Equal(2, rig.Engine.GetSnapshot()!.ActiveFunctions.Count); // preview stops nothing

        rig.Engine.StopPreview();
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetChannelOverride_OverFunctions_OverrideWinsAndClearRestores()
    {
        var rig = new TestRig();
        var preview = rig.Scene("Preview", ("r", 10));
        rig.Load();
        rig.Engine.StartPreview(preview.Id);

        rig.Engine.SetChannelOverride(1, TestRig.Red, 77);
        rig.Engine.SetChannelOverride(1, 500, 33); // unpatched address
        rig.Tick();
        Assert.Equal(77, rig.At(TestRig.Red));
        Assert.Equal(33, rig.At(500));

        rig.Engine.ClearChannelOverride(1, TestRig.Red);
        rig.Tick();
        Assert.Equal(10, rig.At(TestRig.Red));
        Assert.Equal(33, rig.At(500));

        rig.Engine.ClearOverrides();
        rig.Tick();
        Assert.Equal(0, rig.At(500));
    }

    [Fact]
    public void SetChannelOverride_UniverseNotInProject_IsIgnored()
    {
        var rig = new TestRig();
        rig.Load();

        rig.Engine.SetChannelOverride(7, 1, 99);
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 513)]
    [InlineData(0, 1)]
    public void SetChannelOverride_InvalidAddress_Throws(int universe, int address)
    {
        var engine = new DmxEngine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetChannelOverride(universe, address, 1));
    }
}
