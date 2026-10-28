using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Chaser step crossfades: a channel in both the outgoing and the incoming step blends linearly
/// (out + (in − out) × t over the incoming fade-in); a channel in only one step fades from / to whatever is underneath.
/// </summary>
public class ChaserCrossfadeTests
{
    /// <summary>
    /// Loop chaser: step 1 = <paramref name="first"/> (hold 1 s, then <paramref name="fadeOut"/>), step 2 =
    /// <paramref name="second"/> (<paramref name="fadeIn"/>, hold 1 s). The crossfade runs from 1 s to 1 s + fade-in.
    /// </summary>
    private static (TestRig Rig, ChaserFunction Chaser) Crossfade(
        (string Key, byte Value)[] first,
        (string Key, byte Value)[] second,
        double fadeOut = 1,
        double fadeIn = 1)
    {
        var rig = new TestRig();
        var a = rig.Scene("A", first);
        var b = rig.Scene("B", second);
        var chaser = rig.Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(a.Id, 1, 0, fadeOut);
        chaser.AddStep(b.Id, 1, fadeIn, 0);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        return (rig, chaser);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 1)] // only a fade-in set (the common case)
    [InlineData(2, 1)]
    [InlineData(0.5, 1)]
    public void Crossfade_EqualValueInBothSteps_StaysConstant(double fadeOut, double fadeIn)
    {
        var (rig, _) = Crossfade([("w", 200), ("r", 255)], [("w", 200), ("b", 255)], fadeOut, fadeIn);

        rig.Tick(0.9);
        Assert.Equal(200, rig.At(TestRig.White));
        for (var i = 0; i < 30; i++)
        {
            rig.Tick(0.05); // 0.95 s .. 2.4 s: before, through and after the crossfade
            Assert.Equal(200, rig.At(TestRig.White));
        }
    }

    [Fact]
    public void Crossfade_SameRedInRedAndMagentaStep_RedStaysFull()
    {
        var (rig, _) = Crossfade([("r", 255)], [("r", 255), ("b", 255)]);

        rig.Tick(1.5);

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(128, rig.At(TestRig.Blue)); // only in the new step: fades in from 0 (127.5)
    }

    [Theory]
    [InlineData(0.25, 191, 64)] // 191.25 / 63.75
    [InlineData(0.5, 128, 128)] // 127.5 each
    [InlineData(0.75, 64, 191)]
    public void Crossfade_RedToBlueSharingWhite_BlendsLinearly(double progress, byte red, byte blue)
    {
        var (rig, _) = Crossfade([("w", 100), ("r", 255)], [("w", 100), ("b", 255)]);

        rig.Tick(1 + progress);

        Assert.Equal(red, rig.At(TestRig.Red));
        Assert.Equal(blue, rig.At(TestRig.Blue));
        Assert.Equal(100, rig.At(TestRig.White));
    }

    [Theory]
    [InlineData(0.25, 191)] // 255 → 0 shared: 191.25
    [InlineData(0.5, 128)]
    [InlineData(0.75, 64)]
    public void Crossfade_SharedChannelGoingDown_BlendsLinearly(double progress, byte white)
    {
        var (rig, _) = Crossfade([("w", 255)], [("w", 0)]);

        rig.Tick(1 + progress);

        Assert.Equal(white, rig.At(TestRig.White));
    }

    [Fact]
    public void Crossfade_FadeOutZeroFadeInSet_SharedBlendsOverTheFadeIn()
    {
        var (rig, _) = Crossfade([("w", 255), ("r", 255)], [("w", 55), ("b", 255)], fadeOut: 0, fadeIn: 1);

        rig.Tick(1.5);

        Assert.Equal(155, rig.At(TestRig.White)); // 255 + (55 − 255) × 0.5
        Assert.Equal(0, rig.At(TestRig.Red)); // only in the old step, no fade-out: gone at once
        Assert.Equal(128, rig.At(TestRig.Blue));
    }

    [Fact]
    public void Crossfade_FadeInZeroFadeOutSet_SharedSnapsOldOnlyFadesOut()
    {
        var (rig, _) = Crossfade([("w", 255), ("r", 255)], [("w", 55), ("b", 255)], fadeOut: 1, fadeIn: 0);

        rig.Tick(1.5);

        Assert.Equal(55, rig.At(TestRig.White)); // the new step is fully in at once
        Assert.Equal(128, rig.At(TestRig.Red)); // fades out over its own fade-out
        Assert.Equal(255, rig.At(TestRig.Blue));
    }

    [Fact]
    public void Crossfade_ChannelOnlyInNewStep_FadesInFromUnderneath()
    {
        // The dimmer's profile default (255) is underneath; the new step brings it to 100.
        var (rig, _) = Crossfade([("r", 255)], [("dim", 100), ("b", 255)]);

        rig.Tick(1.5);

        Assert.Equal(178, rig.At(TestRig.Dimmer)); // 255 × 0.5 + 100 × 0.5 = 177.5
    }

    [Fact]
    public void Crossfade_ChannelOnlyInOldStep_FadesOutToUnderneath()
    {
        var (rig, _) = Crossfade([("dim", 100), ("r", 255)], [("b", 255)]);

        rig.Tick(1.5);

        Assert.Equal(178, rig.At(TestRig.Dimmer));
        rig.Tick(0.6); // 2.1 s: fade-out over
        Assert.Equal(255, rig.At(TestRig.Dimmer));
    }

    [Fact]
    public void Crossfade_LoopWrapToFirstStep_SharedStaysConstant()
    {
        var rig = new TestRig();
        var a = rig.Scene("A", ("w", 200), ("r", 255));
        var b = rig.Scene("B", ("w", 200), ("b", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(a.Id, 1, 1, 1);
        chaser.AddStep(b.Id, 1, 1, 1);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(4.5); // second pass, step 1 half faded in over step 2

        Assert.Equal(200, rig.At(TestRig.White));
        Assert.Equal(128, rig.At(TestRig.Red));
        Assert.Equal(128, rig.At(TestRig.Blue));
    }

    [Fact]
    public void Crossfade_FirstStep_FadesInFromUnderneath()
    {
        var (rig, chaser) = Crossfade([("w", 200)], [("w", 200)]);
        chaser.Steps[0].FadeInSeconds = 1;
        rig.RefreshAndApply();
        rig.Engine.ReleaseAll();
        rig.Tick(0);
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.5);

        Assert.Equal(100, rig.At(TestRig.White));
    }

    [Fact]
    public void Crossfade_NonIntensityChannel_SwitchesToTheNewValue()
    {
        var (rig, _) = Crossfade([("macro", 50)], [("macro", 150)]);

        rig.Tick(0.9);
        Assert.Equal(50, rig.At(TestRig.Macro));
        rig.Tick(0.2); // 1.1 s: the new step has begun to fade in

        Assert.Equal(150, rig.At(TestRig.Macro));
    }

    [Fact]
    public void Crossfade_NestedChaser_InnerAndOuterCrossfadesBlendLinearly()
    {
        var rig = new TestRig();
        var a1 = rig.Scene("A1", ("w", 200), ("r", 255));
        var a2 = rig.Scene("A2", ("w", 200), ("g", 255));
        var b = rig.Scene("B", ("w", 200), ("b", 255));
        var inner = rig.Add(new ChaserFunction { Name = "Inner" });
        inner.AddStep(a1.Id, 1, 0, 1);
        inner.AddStep(a2.Id, 10, 1, 0);
        var outer = rig.Add(new ChaserFunction { Name = "Outer" });
        outer.AddStep(inner.Id, 3, 0, 1);
        outer.AddStep(b.Id, 10, 1, 0);
        rig.Load();
        rig.Engine.StartFunction(outer.Id, EngineLayer.Console, 0.5);

        rig.Tick(1.5); // inner crossfade A1 → A2
        Assert.Equal(100, rig.At(TestRig.White)); // 200 at intensity 0.5, steady
        Assert.Equal(64, rig.At(TestRig.Red)); // 63.75
        Assert.Equal(64, rig.At(TestRig.Green));

        rig.Tick(2); // 3.5 s: outer crossfade inner (at A2) → B
        Assert.Equal(100, rig.At(TestRig.White));
        Assert.Equal(64, rig.At(TestRig.Green));
        Assert.Equal(64, rig.At(TestRig.Blue));
        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void Crossfade_UnderShowLayer_SharedChannelDoesNotRevealTheShow()
    {
        var rig = new TestRig();
        var under = rig.Scene("Under", ("w", 30));
        var a = rig.Scene("A", ("w", 200));
        var b = rig.Scene("B", ("w", 200));
        var chaser = rig.Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(a.Id, 1, 0, 1);
        chaser.AddStep(b.Id, 1, 1, 0);
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 100;
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, under.Id, 0, 90).SetTiming(0, 90);
        rig.Load();
        rig.Engine.ShowOverrideMode = ShowOverrideMode.TouchedChannelsOnly;
        rig.Engine.StartShow(show.Id);
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(1.5);

        Assert.Equal(200, rig.At(TestRig.White));
    }

    [Fact]
    public void Tick_CrossfadingNestedChasers_AllocatesNothing()
    {
        var rig = new TestRig();
        rig.Project.AddFixtures(TestRig.Profile(), "8ch", 10, 1, 100, "Bar");
        var a = rig.Scene("A", ("w", 200), ("r", 255), ("macro", 10));
        var b = rig.Scene("B", ("w", 200), ("b", 255), ("macro", 30));
        var inner = rig.Add(new ChaserFunction { Name = "Inner" });
        inner.AddStep(a.Id, 0.2, 0.2, 0.3);
        inner.AddStep(b.Id, 0.1, 0.3, 0.1);
        var outer = rig.Add(new ChaserFunction { Name = "Outer", RunOrder = RunOrder.PingPong });
        outer.AddStep(inner.Id, 0.5, 0.2, 0.4);
        outer.AddStep(a.Id, 0.3, 0.4, 0);
        outer.AddStep(b.Id, 0.2, 0, 0.2);
        rig.Load();
        rig.Engine.StartFunction(outer.Id, EngineLayer.Console, 0.8);
        rig.Engine.StartFunction(inner.Id, EngineLayer.Preview);

        var allocated = AllocationProbe.Measure(_ => rig.Tick(), iterations: 2000, warmupPerRound: 200);

        Assert.Equal(0, allocated);
    }
}
