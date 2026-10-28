using DmxPro.Core.Functions;
using DmxPro.Core.Shows;
using DmxPro.Engine.Testing;

namespace DmxPro.Engine.Tests;

/// <summary>Live mode (owner decision 2026-10-22): only the Virtual Console drives the lights.</summary>
public class LiveModeTests
{
    private sealed class Setup
    {
        public required TestRig Rig { get; init; }

        public required SceneFunction Console { get; init; }

        public required SceneFunction Edited { get; init; }

        public required Show GreenShow { get; init; }

        public required Show MacroShow { get; init; }
    }

    /// <summary>
    /// Console scene: blue 255. Edited scene (previewed): red 255. Green show (10 s, item 0–10 s): green 255. Macro show:
    /// macro 77.
    /// </summary>
    private static Setup Create()
    {
        var rig = new TestRig();
        var console = rig.Scene("Console", ("b", 255));
        var edited = rig.Scene("Edited", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var macro = rig.Scene("Macro", ("macro", 77));
        var greenShow = ShowWith(rig, "Green show", green);
        var macroShow = ShowWith(rig, "Macro show", macro);
        rig.Engine.ShowOverrideMode = ShowOverrideMode.TouchedChannelsOnly;
        rig.Load();
        return new Setup { Rig = rig, Console = console, Edited = edited, GreenShow = greenShow, MacroShow = macroShow };
    }

    private static Show ShowWith(TestRig rig, string name, SceneFunction scene)
    {
        var show = rig.Project.CreateShow(name);
        show.LengthSeconds = 10;
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, scene.Id, 0, 10).SetTiming(0, 10, 0, 0);
        return show;
    }

    [Fact]
    public void SetLiveMode_On_SuppressesPreviewOverridesAndEditorShowsWhileConsoleStillOutputs()
    {
        var s = Create();
        var rig = s.Rig;
        rig.Engine.StartFunction(s.Console.Id, EngineLayer.Console);
        rig.Engine.SetPreviewFunction(rig.Project, PreviewDraftTests.DraftOf(s.Edited));
        rig.Engine.SetChannelOverride(1, TestRig.White, 99);
        rig.Engine.StartShow(s.GreenShow.Id, origin: ShowOrigin.Editor);
        rig.Engine.StartShow(s.MacroShow.Id, origin: ShowOrigin.Console);
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(99, rig.At(TestRig.White));
        Assert.Equal(255, rig.At(TestRig.Green));

        rig.Engine.SetLiveMode(true);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red)); // preview gone
        Assert.Equal(0, rig.At(TestRig.White)); // override cleared
        Assert.Equal(0, rig.At(TestRig.Green)); // editor show stopped
        Assert.Equal(255, rig.At(TestRig.Blue)); // console function keeps playing
        Assert.Equal(77, rig.At(TestRig.Macro)); // console show keeps playing
        Assert.Null(rig.Engine.GetShowPosition(s.GreenShow.Id));
        Assert.NotNull(rig.Engine.GetShowPosition(s.MacroShow.Id));
        Assert.DoesNotContain(rig.Engine.GetSnapshot()!.ActiveFunctions, f => f.Layer == EngineLayer.Preview);
    }

    [Fact]
    public void SetLiveMode_On_RefusesNewPreviewsOverridesAndEditorShows()
    {
        var s = Create();
        var rig = s.Rig;
        rig.Engine.SetLiveMode(true);
        rig.Tick();

        Assert.False(rig.Engine.SetPreviewFunction(rig.Project, PreviewDraftTests.DraftOf(s.Edited)));
        rig.Engine.StartPreview(s.Edited.Id);
        rig.Engine.StartFunction(s.Edited.Id, EngineLayer.Preview);
        rig.Engine.SetChannelOverride(1, TestRig.White, 99);
        rig.Engine.StartShow(s.GreenShow.Id, origin: ShowOrigin.Editor);
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        Assert.Null(rig.Engine.GetShowPosition(s.GreenShow.Id));
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void SetLiveMode_On_ConsoleSourcesStillWork()
    {
        var s = Create();
        var rig = s.Rig;
        rig.Engine.SetLiveMode(true);
        rig.Tick();

        rig.Engine.StartFunction(s.Console.Id, EngineLayer.Console);
        rig.Engine.StartShow(s.GreenShow.Id); // console origin by default
        rig.Engine.GrandMaster = 0.5;
        rig.Tick();

        Assert.Equal(128, rig.At(TestRig.Blue));
        Assert.Equal(128, rig.At(TestRig.Green));

        rig.Engine.Blackout = true;
        rig.Tick();
        Assert.Equal(0, rig.At(TestRig.Blue));

        rig.Engine.Blackout = false;
        rig.Engine.GrandMaster = 1;
        rig.Engine.ReleaseAll();
        rig.Tick();
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void SetLiveMode_Off_KeepsConsoleAndAllowsOtherSourcesAgain()
    {
        var s = Create();
        var rig = s.Rig;
        rig.Engine.StartFunction(s.Console.Id, EngineLayer.Console);
        rig.Engine.SetLiveMode(true);
        rig.Tick();

        rig.Engine.SetLiveMode(false);
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Blue)); // nothing jumps

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, PreviewDraftTests.DraftOf(s.Edited)));
        rig.Engine.SetChannelOverride(1, TestRig.White, 99);
        rig.Engine.StartShow(s.GreenShow.Id, origin: ShowOrigin.Editor);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(99, rig.At(TestRig.White));
        Assert.Equal(255, rig.At(TestRig.Green));
        Assert.Equal(255, rig.At(TestRig.Blue));
    }

    [Fact]
    public void SetLiveMode_OnThenOffInSameTick_AppliesInQueueOrder()
    {
        var s = Create();
        var rig = s.Rig;
        rig.Engine.SetChannelOverride(1, TestRig.White, 99);
        rig.Tick();

        rig.Engine.SetLiveMode(true);
        rig.Engine.SetLiveMode(false);
        rig.Engine.SetChannelOverride(1, TestRig.Red, 5);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.White)); // cleared by the switch on
        Assert.Equal(5, rig.At(TestRig.Red)); // accepted after the switch off
        Assert.False(rig.Engine.IsLiveMode);
    }

    [Fact]
    public void SetLiveMode_On_CountsAsOverrideRelease()
    {
        var rig = new TestRig();
        rig.Load();
        var before = rig.Engine.ReleaseGeneration;

        rig.Engine.SetLiveMode(true);

        Assert.True(rig.Engine.ReleaseGeneration > before);
        Assert.True(rig.Engine.IsLiveMode);
    }

    [Fact]
    public void ChannelTestSession_LiveModeOn_ForgetsAndRefusesTestValues()
    {
        var rig = new TestRig();
        rig.Load();
        var session = new ChannelTestSession(rig.Engine);
        Assert.True(session.Set(rig.Fixture.Id, "r", 100));
        rig.Tick();
        Assert.Equal(100, rig.At(TestRig.Red));

        rig.Engine.SetLiveMode(true);
        rig.Tick();

        Assert.Equal(1, session.Sync());
        Assert.False(session.Set(rig.Fixture.Id, "r", 50));
        session.Reapply();
        rig.Tick();
        Assert.Equal(0, rig.At(TestRig.Red));

        rig.Engine.SetLiveMode(false);
        Assert.True(session.Set(rig.Fixture.Id, "r", 50));
        rig.Tick();
        Assert.Equal(50, rig.At(TestRig.Red));
    }

    [Fact]
    public void StartShow_EditorOrigin_PlaysWhenNotLive()
    {
        var s = Create();
        var rig = s.Rig;

        rig.Engine.StartShow(s.GreenShow.Id, TimeSpan.FromSeconds(1), ShowOrigin.Editor);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Green));
    }

    [Fact]
    public void Tick_LiveModeOnWithConsoleRunning_AllocatesNothing()
    {
        var s = Create();
        var rig = s.Rig;
        rig.Engine.StartFunction(s.Console.Id, EngineLayer.Console);
        rig.Engine.SetLiveMode(true);
        Assert.Equal(0, AllocationProbe.Measure(_ => rig.Tick()));
    }
}
