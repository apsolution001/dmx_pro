using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Bug hunt: show rendering with <c>FirstLiveIndex</c> + early break and shared (interned) scene arrays (these pass and
/// pin the behaviour the optimisation must keep), plus hardware strobe on a .qxf-style channel.
/// </summary>
public class BugHuntShowRenderTests
{
    [Fact]
    public void ShowRender_LongItemStillLiveAfterShortLaterItemsEnded_LongItemPlaysAndSeekBackRestoresShortOnes()
    {
        var rig = new TestRig();
        var longScene = rig.Scene("Long", ("r", 200));
        var shortA = rig.Scene("A", ("g", 100));
        var shortB = rig.Scene("B", ("b", 50));
        var sameStart = rig.Scene("Same", ("w", 30));
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 100;
        var track2 = rig.Project.AddTrack(show.Id, "T2");
        var track3 = rig.Project.AddTrack(show.Id, "T3");
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, shortA.Id, 1, 1);  // 1–2
        rig.Project.PlaceFunctionInShow(show.Id, track2.Id, longScene.Id, 0.5, 50);    // 0.5–50.5
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, shortB.Id, 3, 1);  // 3–4
        rig.Project.PlaceFunctionInShow(show.Id, track3.Id, sameStart.Id, 3, 10);      // 3–13 (same start as B)
        rig.Load();
        rig.Engine.StartShow(show.Id);

        rig.Tick(10);
        Assert.Equal(200, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green));
        Assert.Equal(0, rig.At(TestRig.Blue));
        Assert.Equal(30, rig.At(TestRig.White));

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(3.5));
        rig.Tick(0);
        Assert.Equal(200, rig.At(TestRig.Red));
        Assert.Equal(50, rig.At(TestRig.Blue));
        Assert.Equal(30, rig.At(TestRig.White));

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(1.5));
        rig.Tick(0);
        Assert.Equal(200, rig.At(TestRig.Red));
        Assert.Equal(100, rig.At(TestRig.Green));
        Assert.Equal(0, rig.At(TestRig.Blue));

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(60));
        rig.Tick(0);
        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void Refresh_EditOneOfTwoEqualSceneCopies_OtherCopyKeepsItsValues()
    {
        var rig = new TestRig();
        var a = rig.Scene("A", ("macro", 40));
        var b = rig.Scene("B", ("macro", 40));
        rig.Load();

        // Equal content -> interned arrays shared at compile time; editing one must not leak into the other.
        a.SetValue(rig.Fixture.Id, "macro", 90);
        rig.RefreshAndApply();
        rig.Engine.StartFunction(b.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(40, rig.At(TestRig.Macro));
    }
}
