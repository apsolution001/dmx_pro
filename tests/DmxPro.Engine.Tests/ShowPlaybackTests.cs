using DmxPro.Core.Shows;

namespace DmxPro.Engine.Tests;

public class ShowPlaybackTests
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

    [Fact]
    public void StartShow_ItemTiming_FollowsStartEndAndFadeWedges()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);

        rig.Tick(1);
        Assert.Equal(0, rig.At(TestRig.Red)); // before start

        rig.Tick(1.5);
        Assert.Equal(128, rig.At(TestRig.Red)); // 2.5 s: half way up the fade-in wedge

        rig.Tick(1);
        Assert.Equal(255, rig.At(TestRig.Red)); // 3.5 s: fully in

        rig.Tick(2);
        Assert.Equal(128, rig.At(TestRig.Red)); // 5.5 s: half way down the fade-out wedge

        rig.Tick(1);
        Assert.Equal(0, rig.At(TestRig.Red)); // 6.5 s: released
        Assert.Equal(TimeSpan.FromSeconds(6.5), rig.Engine.GetShowPosition(show.Id));
    }

    [Fact]
    public void StartShow_PastLength_StopsByItself()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);

        rig.Tick(9.9);
        Assert.NotNull(rig.Engine.GetShowPosition(show.Id));

        rig.Tick(0.2);
        Assert.Null(rig.Engine.GetShowPosition(show.Id));
        Assert.Empty(rig.Engine.GetSnapshot()!.Shows);
    }

    [Fact]
    public void StartShow_WithStartPosition_PlaysFromThere()
    {
        var (rig, show) = RigWithShow();

        rig.Engine.StartShow(show.Id, TimeSpan.FromSeconds(4));
        rig.Tick(0);

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void StopShow_Playing_ReleasesItems()
    {
        var (rig, show) = RigWithShow();
        rig.Engine.StartShow(show.Id);
        rig.Tick(4);
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Engine.StopShow(show.Id);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Null(rig.Engine.GetShowPosition(show.Id));
    }

    [Fact]
    public void StartShow_ConsoleFunctionOnTop_ConsoleWinsThenShowReturns()
    {
        var (rig, show) = RigWithShow();
        var blackRed = rig.Scene("No red", ("r", 0));
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id);
        rig.Tick(4);

        rig.Engine.StartFunction(blackRed.Id, EngineLayer.Console);
        rig.Tick(0.1);
        Assert.Equal(0, rig.At(TestRig.Red));

        rig.Engine.StopFunction(blackRed.Id, EngineLayer.Console);
        rig.Tick(0.1);
        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void StartShow_OverlappingItems_LaterItemWinsLtp()
    {
        var rig = new TestRig();
        var first = rig.Scene("First", ("macro", 10));
        var second = rig.Scene("Second", ("macro", 20));
        var show = rig.Project.CreateShow("Show");
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, first.Id, 0, 10);
        var track2 = rig.Project.AddTrack(show.Id, "Track 2");
        rig.Project.PlaceFunctionInShow(show.Id, track2.Id, second.Id, 2, 2);
        rig.Load();
        rig.Engine.StartShow(show.Id);

        rig.Tick(1);
        Assert.Equal(10, rig.At(TestRig.Macro));

        rig.Tick(2);
        Assert.Equal(20, rig.At(TestRig.Macro));

        rig.Tick(2);
        Assert.Equal(10, rig.At(TestRig.Macro));
    }
}
