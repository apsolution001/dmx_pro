using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Safety rule 8 for chasers: a step change happens no sooner than 1 / (2 × max strobe rate) after the previous one
/// (real time), and no sooner than 1 / (2 × <see cref="ChaserSpeedLimit.SafeFlashRateHz"/>) while strobe is not allowed.
/// </summary>
public class ChaserSpeedLimitTests
{
    private const double HundredthSecond = 0.01;

    /// <summary>Red/Green chaser with very short steps on the rig fixture.</summary>
    private static (TestRig Rig, ChaserFunction Chaser) FastChaserRig(double holdSeconds = HundredthSecond, RunOrder order = RunOrder.Loop)
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Fast", RunOrder = order });
        chaser.AddStep(red.Id, holdSeconds);
        chaser.AddStep(green.Id, holdSeconds);
        return (rig, chaser);
    }

    private static bool IsRed(TestRig rig) => rig.At(TestRig.Red) == 255 && rig.At(TestRig.Green) == 0;

    private static bool IsGreen(TestRig rig) => rig.At(TestRig.Green) == 255 && rig.At(TestRig.Red) == 0;

    /// <summary>Ticks <paramref name="seconds"/> in 5 ms frames and returns the number of colour changes seen.</summary>
    private static int CountChanges(TestRig rig, double seconds)
    {
        var changes = 0;
        var last = rig.At(TestRig.Red);
        for (var i = 0; i < (int)Math.Round(seconds / 0.005); i++)
        {
            rig.Tick(0.005);
            var now = rig.At(TestRig.Red);
            if (now != last)
            {
                changes++;
                last = now;
            }
        }

        return changes;
    }

    // ---- The rule -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, 10, 0.05)]
    [InlineData(true, 25, 0.02)]
    [InlineData(true, 5, 0.1)]
    [InlineData(false, 10, 1.0 / 6)]
    [InlineData(false, 25, 1.0 / 6)]
    [InlineData(false, 2, 0.25)]
    [InlineData(true, 0, 1.0 / 6)]
    [InlineData(true, -3, 1.0 / 6)]
    [InlineData(true, double.NaN, 1.0 / 6)]
    public void MinStepSeconds_StrobeSettings_IsHalfAFlashPeriod(bool allowStrobe, double maxRateHz, double expected)
    {
        Assert.Equal(expected, ChaserSpeedLimit.MinStepSeconds(allowStrobe, maxRateHz), 12);
    }

    // ---- Playback -------------------------------------------------------------------------------------------------------

    [Fact]
    public void StartFunction_HundredthSecondSteps_CappedToMaxStrobeRate()
    {
        var (rig, chaser) = FastChaserRig();
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 5; // steps of at least 0.1 s
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.075);
        Assert.True(IsRed(rig)); // uncapped (0.05 s minimum) it would already be green
        rig.Tick(0.05); // 0.125 s
        Assert.True(IsGreen(rig));
        rig.Tick(0.05); // 0.175 s
        Assert.True(IsGreen(rig));
        rig.Tick(0.05); // 0.225 s
        Assert.True(IsRed(rig));

        var info = Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.Equal(StrobeLimit.RateLimited, info.StrobeLimit);
        Assert.Equal(StrobeLimit.RateLimited, rig.Engine.GetSnapshot()!.StrobeLimit);
    }

    [Fact]
    public void StartFunction_HundredthSecondSteps_NeverFlashesFasterThanMaxRate()
    {
        var (rig, chaser) = FastChaserRig();
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 8;
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        var changes = CountChanges(rig, 4);

        // 8 flashes per second = 16 colour changes per second at most.
        Assert.InRange(changes, 60, 65);
    }

    [Fact]
    public void StartFunction_StrobeNotAllowed_StepsAtLeastOneSixthSecond()
    {
        var (rig, chaser) = FastChaserRig();
        rig.Load();
        Assert.False(rig.Engine.AllowStrobe);
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.15);
        Assert.True(IsRed(rig));
        rig.Tick(0.025); // 0.175 s
        Assert.True(IsGreen(rig));
        rig.Tick(0.15); // 0.325 s
        Assert.True(IsGreen(rig));
        rig.Tick(0.025); // 0.35 s
        Assert.True(IsRed(rig));
        Assert.Equal(StrobeLimit.RateLimited, Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions).StrobeLimit);
    }

    [Fact]
    public void StartFunction_StrobeNotAllowed_AtMostThreeFlashesPerSecond()
    {
        var (rig, chaser) = FastChaserRig();
        rig.Load();
        rig.Engine.MaxStrobeRateHz = 25; // ignored while strobe is not allowed
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        Assert.InRange(CountChanges(rig, 4), 22, 25);
    }

    [Fact]
    public void StartFunction_StrobeNotAllowedAndLowerMaxRate_UsesTheLowerRate()
    {
        var (rig, chaser) = FastChaserRig();
        rig.Load();
        rig.Engine.MaxStrobeRateHz = 2; // steps of at least 0.25 s
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.225);
        Assert.True(IsRed(rig));
        rig.Tick(0.05); // 0.275 s
        Assert.True(IsGreen(rig));
    }

    [Fact]
    public void SetFunctionSpeed_SpedUpChaserAboveMaxRate_CappedInRealTime()
    {
        // 0.2 s steps are fine at speed 1, but at speed 4 they would last 0.05 s of real time.
        var (rig, chaser) = FastChaserRig(holdSeconds: 0.2);
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 5;
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 4);

        rig.Tick(0.075);
        Assert.True(IsRed(rig));
        rig.Tick(0.05); // 0.125 s
        Assert.True(IsGreen(rig));
        rig.Tick(0.1); // 0.225 s
        Assert.True(IsRed(rig));
        Assert.Equal(StrobeLimit.RateLimited, Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions).StrobeLimit);
    }

    [Fact]
    public void StartFunction_StepsLongerThanLimit_PlayAsProgrammedWithoutLimit()
    {
        var (rig, chaser) = FastChaserRig(holdSeconds: 0.2);
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 5;
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.175);
        Assert.True(IsRed(rig));
        rig.Tick(0.05); // 0.225 s
        Assert.True(IsGreen(rig));
        Assert.Equal(StrobeLimit.None, Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions).StrobeLimit);
        Assert.Equal(StrobeLimit.None, rig.Engine.GetSnapshot()!.StrobeLimit);
    }

    [Fact]
    public void StartFunction_CappedChaser_StoredStepTimesUnchanged()
    {
        var (rig, chaser) = FastChaserRig();
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(1);

        Assert.All(chaser.Steps, step => Assert.Equal(HundredthSecond, step.HoldSeconds));
    }

    [Fact]
    public void StartFunction_SingleShotCapped_PlaysEveryStepAtTheLimitThenReleases()
    {
        var (rig, chaser) = FastChaserRig(order: RunOrder.SingleShot);
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 5;
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.15); // uncapped the whole chaser would be over after 0.1 s
        Assert.True(IsGreen(rig));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);

        rig.Tick(0.1); // 0.25 s: both 0.1 s steps are over
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StartFunction_NestedFastChaser_IsCappedToo()
    {
        var (rig, inner) = FastChaserRig();
        var outer = rig.Add(new ChaserFunction { Name = "Outer" });
        outer.AddStep(inner.Id, 10);
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 5;
        rig.Engine.StartFunction(outer.Id, EngineLayer.Console);

        rig.Tick(0.075);
        Assert.True(IsRed(rig));
        rig.Tick(0.05); // 0.125 s
        Assert.True(IsGreen(rig));
        Assert.Equal(StrobeLimit.RateLimited, Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions).StrobeLimit);
    }

    [Fact]
    public void SetPreviewFunction_FastChaserDraft_IsCapped()
    {
        var (rig, chaser) = FastChaserRig(holdSeconds: 1);
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 5;
        var draft = PreviewDraftTests.DraftOf(chaser);
        foreach (var step in draft.Steps)
        {
            step.HoldSeconds = HundredthSecond; // edited in the draft
        }

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft));
        rig.Tick(0.075);
        Assert.True(IsRed(rig));
        rig.Tick(0.05); // 0.125 s
        Assert.True(IsGreen(rig));
        var preview = Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions, f => f.Layer == EngineLayer.Preview);
        Assert.Equal(StrobeLimit.RateLimited, preview.StrobeLimit);
    }

    [Fact]
    public void StartShow_FastChaserItem_IsCappedAndReported()
    {
        var (rig, chaser) = FastChaserRig();
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 20;
        var item = rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, chaser.Id, 0, 10);
        item.SetTiming(0, 10);
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 5;
        rig.Engine.StartShow(show.Id);

        rig.Tick(0.075);
        Assert.True(IsRed(rig));
        rig.Tick(0.05); // 0.125 s
        Assert.True(IsGreen(rig));
        Assert.Equal(StrobeLimit.RateLimited, rig.Engine.GetSnapshot()!.StrobeLimit);
    }

    [Fact]
    public void AllowStrobe_TurnedOnWhileCapped_FasterLimitAppliesAtOnce()
    {
        var (rig, chaser) = FastChaserRig();
        rig.Load();
        rig.Engine.MaxStrobeRateHz = 5;
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        Assert.InRange(CountChanges(rig, 2), 11, 13); // 3 Hz while not allowed

        rig.Engine.AllowStrobe = true;

        Assert.InRange(CountChanges(rig, 2), 18, 22); // 5 Hz
    }

    [Fact]
    public void Tick_StretchedChaserWithChangingLimit_AllocatesNothing()
    {
        var (rig, chaser) = FastChaserRig();
        var outer = rig.Add(new ChaserFunction { Name = "Outer", RunOrder = RunOrder.PingPong });
        outer.AddStep(chaser.Id, 0.5, 0.1, 0.1);
        outer.AddStep(chaser.Id, 0.3);
        outer.AddStep(chaser.Id, HundredthSecond);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Engine.StartFunction(outer.Id, EngineLayer.Console);

        var allocated = AllocationProbe.Measure(i =>
        {
            rig.Engine.AllowStrobe = i % 7 < 4;
            rig.Engine.MaxStrobeRateHz = 1 + (i % 11);
            rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, i % 5);
            rig.Tick();
        });

        Assert.Equal(0, allocated);
    }
}
