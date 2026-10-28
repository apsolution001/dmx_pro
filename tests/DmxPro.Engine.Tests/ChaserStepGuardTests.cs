using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Safety rule 8 for running chasers: a speed or limit change only changes the rate from then on (the chaser keeps its
/// step), and a per-instance backstop never shows two step changes closer than the real-time minimum.
/// </summary>
public class ChaserStepGuardTests
{
    private const double OneSixth = 1.0 / 6;

    // A tick can land a change up to one frame late, never early: allow only rounding.
    private const double Tolerance = 1e-6;

    /// <summary>Records the engine times at which the rig fixture's red/green/blue output changes.</summary>
    private sealed class ChangeRecorder(TestRig rig)
    {
        private (byte, byte, byte) _last = (rig.At(TestRig.Red), rig.At(TestRig.Green), rig.At(TestRig.Blue));

        public double Now { get; private set; }

        public List<double> Changes { get; } = [];

        public void Tick(double seconds)
        {
            rig.Tick(seconds);
            Now += seconds;
            var rgb = (rig.At(TestRig.Red), rig.At(TestRig.Green), rig.At(TestRig.Blue));
            if (rgb != _last)
            {
                Changes.Add(Now);
                _last = rgb;
            }
        }

        /// <summary>
        /// Smallest time between two step changes. The first recorded change is the chaser coming on (it started at the
        /// tick before, so its first step is timed from the start command), not a step change.
        /// </summary>
        public double SmallestGap()
        {
            var smallest = double.PositiveInfinity;
            for (var i = 2; i < Changes.Count; i++)
            {
                smallest = Math.Min(smallest, Changes[i] - Changes[i - 1]);
            }

            return smallest;
        }
    }

    private static (TestRig Rig, ChaserFunction Chaser) FastChaser(double holdSeconds = 0.05)
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Fast" });
        chaser.AddStep(red.Id, holdSeconds);
        chaser.AddStep(green.Id, holdSeconds);
        return (rig, chaser);
    }

    [Fact]
    public void SetFunctionSpeed_SweepAfterLongRun_NoChangesFasterThanTheLimit()
    {
        var (rig, chaser) = FastChaser();
        rig.Load();
        Assert.False(rig.Engine.AllowStrobe); // steps of at least 1/6 s
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        var recorder = new ChangeRecorder(rig);
        for (var i = 0; i < 1500; i++)
        {
            recorder.Tick(0.02); // 30 s at speed 1
        }

        var sweepStart = recorder.Now;
        var changesBefore = recorder.Changes.Count;
        for (var i = 0; i <= 20; i++)
        {
            rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 1 + (i * 0.01));
            recorder.Tick(0.02);
        }

        for (var i = 0; i < 50; i++)
        {
            recorder.Tick(0.02);
        }

        Assert.True(recorder.SmallestGap() >= OneSixth - Tolerance, $"smallest gap {recorder.SmallestGap()}");

        // The sweep (0.42 s) only changes the rate: about 0.42 × 6 changes, not one per tick.
        var duringSweep = recorder.Changes.Count(t => t > sweepStart && t <= sweepStart + 0.42);
        Assert.InRange(duringSweep, 1, 3);
        Assert.True(recorder.Changes.Count > changesBefore);
    }

    [Fact]
    public void SetFunctionSpeed_SlowerAndFaster_KeepsTheCurrentStep()
    {
        // 2 s steps: a re-timing that jumped would land in the other step.
        var (rig, chaser) = FastChaser(holdSeconds: 2);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(101); // pass 25, red at 1 s of 2
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 0.5);
        rig.Tick(0.01);
        Assert.Equal(255, rig.At(TestRig.Red));
        rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 3);
        rig.Tick(0.01);
        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void AllowStrobe_ChangedWhileStretched_KeepsTheCurrentStep()
    {
        var (rig, chaser) = FastChaser(holdSeconds: 0.01);
        rig.Load();
        rig.Engine.MaxStrobeRateHz = 2; // 0.25 s steps
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(30.1); // red, 0.1 s into its 0.25 s
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Engine.MaxStrobeRateHz = 2.1; // the stretched timeline shrinks a little
        rig.Tick(0.01);

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void StartFunction_NestedChaserRestartedByParentStep_NoChangesFasterThanTheLimit()
    {
        // Inner: red/green of 0.01 s, stretched to 1/6 s each. Outer: one step of 1/6 s + 0.02 s that restarts the inner
        // chaser every pass, so red → green comes 1/6 s in and green → red only 0.02 s later.
        var (rig, inner) = FastChaser(holdSeconds: 0.01);
        var outer = rig.Add(new ChaserFunction { Name = "Outer" });
        outer.AddStep(inner.Id, OneSixth + 0.02);
        rig.Load();
        rig.Engine.StartFunction(outer.Id, EngineLayer.Console);
        var recorder = new ChangeRecorder(rig);

        for (var i = 0; i < 1000; i++)
        {
            recorder.Tick(0.005);
        }

        Assert.True(recorder.Changes.Count >= 10);
        Assert.True(recorder.SmallestGap() >= OneSixth - Tolerance, $"smallest gap {recorder.SmallestGap()}");
    }

    [Fact]
    public void SetPreviewFunction_DraftEditedEveryFrame_NoChangesFasterThanTheLimit()
    {
        var (rig, chaser) = FastChaser(holdSeconds: 0.3);
        rig.Load();
        var recorder = new ChangeRecorder(rig);

        for (var i = 0; i < 250; i++)
        {
            // Hold switches between 0.2 s and 0.3 s on every frame (a slider being dragged in the editor).
            var draft = PreviewDraftTests.DraftOf(chaser);
            foreach (var step in draft.Steps)
            {
                step.HoldSeconds = i % 2 == 0 ? 0.2 : 0.3;
            }

            Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft));
            recorder.Tick(0.02);
        }

        // 5 s of 0.2–0.3 s steps: about 20 changes, never one per frame.
        Assert.InRange(recorder.Changes.Count, 12, 26);
        Assert.True(recorder.SmallestGap() >= OneSixth - Tolerance, $"smallest gap {recorder.SmallestGap()}");
    }

    [Fact]
    public void HasEnded_SingleShotWithSpeedChange_EndsAndReleases()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Once", RunOrder = RunOrder.SingleShot });
        chaser.AddStep(red.Id, 1);
        chaser.AddStep(green.Id, 1);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.5);
        rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 2);
        rig.Tick(0.5); // function time 1.5 s: green
        Assert.Equal(255, rig.At(TestRig.Green));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);

        rig.Tick(0.3); // 2.1 s: over
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void ReleaseAll_RunningGuardedChaser_ClearsAndRestartsFromTheFirstStep()
    {
        var (rig, chaser) = FastChaser(holdSeconds: 0.01);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(0.2); // green
        Assert.Equal(255, rig.At(TestRig.Green));

        rig.Engine.ReleaseAll();
        rig.Tick(0.01);
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());

        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(0.01);
        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void Tick_GuardedChasersWithSpeedSweepAndLimitChanges_AllocatesNothing()
    {
        var (rig, inner) = FastChaser(holdSeconds: 0.01);
        var outer = rig.Add(new ChaserFunction { Name = "Outer" });
        outer.AddStep(inner.Id, OneSixth + 0.02, 0.05, 0.05);
        rig.Load();
        rig.Engine.StartFunction(inner.Id, EngineLayer.Console);
        rig.Engine.StartFunction(outer.Id, EngineLayer.Console);

        var allocated = AllocationProbe.Measure(i =>
        {
            rig.Engine.SetFunctionSpeed(inner.Id, EngineLayer.Console, 1 + ((i % 20) * 0.01));
            rig.Engine.AllowStrobe = i % 30 < 15;
            rig.Tick(0.005);
        });

        Assert.Equal(0, allocated);
    }
}
