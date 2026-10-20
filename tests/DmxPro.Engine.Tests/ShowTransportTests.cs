using DmxPro.Core.Shows;

namespace DmxPro.Engine.Tests;

/// <summary>Pause / resume / seek of shows and the "console override mutes the show" mode (owner decision 2026-10-18).</summary>
public class ShowTransportTests
{
    /// <summary>A 10 s show with one red item from 2 s to 6 s, 1 s fade in and 1 s fade out.</summary>
    private static (TestRig Rig, Show Show) RigWithShow()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 10;
        var item = rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, red.Id, 2, 4);
        item.SetTiming(2, 4, 1, 1);
        rig.Load();
        return (rig, show);
    }

    private static ShowPlaybackInfo Info(TestRig rig, Guid showId)
    {
        Assert.True(rig.Engine.TryGetShowPlayback(showId, out var info, out _));
        return info;
    }

    private static void AssertPosition(TestRig rig, Guid showId, double seconds) =>
        Assert.Equal(seconds, rig.Engine.GetShowPosition(showId)!.Value.TotalSeconds, 6);

    [Fact]
    public void PauseShow_Playing_FreezesPositionAndHoldsValues()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(2.5);
        Assert.Equal(128, rig.At(TestRig.Red));

        rig.Engine.PauseShow(show.Id);
        rig.Tick();
        rig.Tick(3);

        Assert.Equal(128, rig.At(TestRig.Red));
        AssertPosition(rig, show.Id, 2.5);
        Assert.True(Info(rig, show.Id).IsPaused);
    }

    [Fact]
    public void ResumeShow_Paused_ContinuesFromFrozenPosition()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(2.5);
        rig.Engine.PauseShow(show.Id);
        rig.Tick(3);

        rig.Engine.ResumeShow(show.Id);
        rig.Tick(1);

        AssertPosition(rig, show.Id, 3.5);
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.False(Info(rig, show.Id).IsPaused);
    }

    [Fact]
    public void PauseShow_PausedPastLength_DoesNotStop()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(9.5));
        rig.Engine.PauseShow(show.Id);

        rig.Tick(5);

        AssertPosition(rig, show.Id, 9.5);
    }

    [Fact]
    public void SeekShow_IntoFadeOut_RendersFadeLevelAtNewPosition()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(0.5);
        Assert.Equal(0, rig.At(TestRig.Red));

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(5.5));
        rig.Tick(0);

        Assert.Equal(128, rig.At(TestRig.Red));
        AssertPosition(rig, show.Id, 5.5);
    }

    [Fact]
    public void SeekShow_IntoFullItem_RendersFullAndKeepsPlaying()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(8);

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Tick(0.5);

        Assert.Equal(255, rig.At(TestRig.Red));
        AssertPosition(rig, show.Id, 4.5);
        Assert.False(Info(rig, show.Id).IsPaused);
    }

    [Fact]
    public void SeekShow_WhilePaused_StaysPausedAtNewPosition()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(1);
        rig.Engine.PauseShow(show.Id);
        rig.Tick();

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(2.5));
        rig.Tick(2);

        Assert.Equal(128, rig.At(TestRig.Red));
        AssertPosition(rig, show.Id, 2.5);
        Assert.True(Info(rig, show.Id).IsPaused);
    }

    [Fact]
    public void SeekShow_PastLengthWhilePlaying_ClampsAndStops()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(1);

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(50));
        rig.Tick(0);

        Assert.Null(rig.Engine.GetShowPosition(show.Id));
    }

    [Fact]
    public void SeekShow_PastLengthWhilePaused_ClampsToLength()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Engine.PauseShow(show.Id);

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(50));
        rig.Tick(1);

        AssertPosition(rig, show.Id, 10);
    }

    [Fact]
    public void SeekShow_NotPlaying_DoesNothing()
    {
        var (rig, show) = RigWithShow();

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Engine.PauseShow(show.Id);
        rig.Engine.ResumeShow(show.Id);
        rig.Tick();

        Assert.Null(rig.Engine.GetShowPosition(show.Id));
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StartShow_WhilePaused_RestartsUnpaused()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(3);
        rig.Engine.PauseShow(show.Id);
        rig.Tick();

        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(1));
        rig.Tick(1);

        AssertPosition(rig, show.Id, 2);
        Assert.False(Info(rig, show.Id).IsPaused);
    }

    [Fact]
    public void StartShow_ReachesEnd_StopsAndReleases()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Tick(0);
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Tick(6.1);

        Assert.Null(rig.Engine.GetShowPosition(show.Id));
        Assert.False(rig.Engine.TryGetShowPlayback(show.Id, out _, out _));
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void ReleaseAll_PausedShow_StopsIt()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(3);
        rig.Engine.PauseShow(show.Id);
        rig.Tick();

        rig.Engine.ReleaseAll();
        rig.Tick();

        Assert.Null(rig.Engine.GetShowPosition(show.Id));
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());

        // A fresh start is not paused any more.
        rig.Engine.StartShow(show.Id);
        rig.Tick(1);
        AssertPosition(rig, show.Id, 1);
    }

    [Fact]
    public void ShowOverrideMode_Default_IsMuteWholeShow()
    {
        Assert.Equal(ShowOverrideMode.MuteWholeShow, new DmxEngine().ShowOverrideMode);
    }

    [Fact]
    public void ShowOverrideMode_UnknownValue_Throws()
    {
        var engine = new DmxEngine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.ShowOverrideMode = (ShowOverrideMode)42);
    }

    [Fact]
    public void MuteWholeShow_ConsoleFunctionActive_MutesAllShowChannelsAndTimelineKeepsRunning()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id);
        rig.Tick(4);
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Tick(1.5);

        // Red is not touched by the console, but the whole show is muted: only defaults + the console remain.
        var expected = TestRig.DefaultsFrame();
        expected[TestRig.Green - 1] = 200;
        Assert.Equal(expected, rig.Universe());
        AssertPosition(rig, show.Id, 5.5);
        Assert.True(Info(rig, show.Id).IsMuted);
        Assert.True(rig.Engine.GetSnapshot()!.IsShowOverridden);

        rig.Engine.StopFunction(green.Id, EngineLayer.Console);
        rig.Tick(0);

        // The show reappears at its advanced position (5.5 s = half way down the fade-out wedge).
        Assert.Equal(128, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green));
        Assert.False(Info(rig, show.Id).IsMuted);
        Assert.False(rig.Engine.GetSnapshot()!.IsShowOverridden);
    }

    [Fact]
    public void MuteWholeShow_ShowItemEndsWhileMuted_ShowReturnsWithoutIt()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id);
        rig.Tick(4);

        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Tick(3);
        rig.Engine.StopFunction(green.Id, EngineLayer.Console);
        rig.Tick(0);

        AssertPosition(rig, show.Id, 7);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void MuteWholeShow_ConsoleFadingOut_ShowStaysMutedUntilFadeEnds()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        green.FadeOutSeconds = 1;
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(3));
        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Tick(0.1);

        rig.Engine.StopFunction(green.Id, EngineLayer.Console);
        rig.Tick(0.5);
        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(100, rig.At(TestRig.Green));

        rig.Tick(0.6);
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green));
    }

    [Fact]
    public void MuteWholeShow_ConsoleAtZeroIntensity_DoesNotMute()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Engine.StartFunction(green.Id, EngineLayer.Console, 0);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.False(Info(rig, show.Id).IsMuted);
    }

    [Fact]
    public void MuteWholeShow_PreviewOnly_DoesNotMute()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Engine.StartPreview(green.Id);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(200, rig.At(TestRig.Green));
    }

    [Fact]
    public void MuteWholeShow_PausedShow_IsMutedAndHoldsPositionAfterRelease()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Engine.PauseShow(show.Id);
        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Tick(2);
        Assert.Equal(0, rig.At(TestRig.Red));

        rig.Engine.StopFunction(green.Id, EngineLayer.Console);
        rig.Tick(0);

        Assert.Equal(255, rig.At(TestRig.Red));
        AssertPosition(rig, show.Id, 4);
    }

    [Fact]
    public void MuteWholeShow_TwoConsoleFunctions_BothMergedAndShowMutedUntilBothReleased()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200), ("macro", 10));
        var blue = rig.Scene("Blue", ("b", 150), ("macro", 30));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(3));
        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Tick();
        rig.Engine.StartFunction(blue.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(200, rig.At(TestRig.Green));
        Assert.Equal(150, rig.At(TestRig.Blue));
        Assert.Equal(30, rig.At(TestRig.Macro)); // LTP: started last

        rig.Engine.StopFunction(blue.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(200, rig.At(TestRig.Green));
        Assert.Equal(10, rig.At(TestRig.Macro));

        rig.Engine.StopFunction(green.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green));
    }

    [Fact]
    public void MuteWholeShow_GrandMasterAndBlackout_StillApply()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Engine.GrandMaster = 0.5;
        rig.Tick();
        Assert.Equal(100, rig.At(TestRig.Green));

        rig.Engine.Blackout = true;
        rig.Tick();
        Assert.Equal(0, rig.At(TestRig.Green));
        Assert.Equal(0, rig.At(TestRig.Dimmer));
    }

    [Fact]
    public void TouchedChannelsOnly_ConsoleFunctionActive_ShowStaysOnUntouchedChannels()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.ShowOverrideMode = ShowOverrideMode.TouchedChannelsOnly;
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(200, rig.At(TestRig.Green));
        Assert.False(Info(rig, show.Id).IsMuted);
        Assert.False(rig.Engine.GetSnapshot()!.IsShowOverridden);
    }

    [Fact]
    public void ShowOverrideMode_SwitchedWhileOverriding_TakesEffectNextTick()
    {
        var (rig, show) = RigWithShow();
        var green = rig.Scene("Green", ("g", 200));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Engine.StartFunction(green.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(0, rig.At(TestRig.Red));

        rig.Engine.ShowOverrideMode = ShowOverrideMode.TouchedChannelsOnly;
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void TryGetShowPlayback_BeforeFirstTick_ReturnsFalseWithNoSequence()
    {
        var engine = new DmxEngine();

        Assert.False(engine.TryGetShowPlayback(Guid.NewGuid(), out _, out var sequence));
        Assert.Equal(-1, sequence);
    }

    [Fact]
    public void TryGetShowPlayback_Playing_ReportsLengthAndSequence()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(1);

        Assert.True(rig.Engine.TryGetShowPlayback(show.Id, out var info, out var sequence));
        Assert.Equal(TimeSpan.FromSeconds(10), info.Length);
        Assert.Equal(rig.Engine.TickCount, sequence);
    }

    [Fact]
    public void CanPlayShow_KnownAndUnknown_ReportsExistence()
    {
        var (rig, show) = RigWithShow();

        Assert.True(rig.Engine.CanPlayShow(show.Id));
        Assert.False(rig.Engine.CanPlayShow(Guid.NewGuid()));
    }
}
