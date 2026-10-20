using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

public class ChaserPlaybackTests
{
    /// <summary>Scenes Red (r 255), Green (g 255) and Blue (b 255) on the rig fixture, plus a chaser over them.</summary>
    private static (TestRig Rig, ChaserFunction Chaser) RigWithChaser(RunOrder order = RunOrder.Loop, RunDirection direction = RunDirection.Forward, int steps = 2)
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var blue = rig.Scene("Blue", ("b", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Chase", RunOrder = order, Direction = direction });
        chaser.AddStep(red.Id);
        chaser.AddStep(green.Id);
        if (steps > 2)
        {
            chaser.AddStep(blue.Id);
        }

        rig.Load();
        return (rig, chaser);
    }

    private static (byte R, byte G, byte B) Rgb(TestRig rig) => (rig.At(TestRig.Red), rig.At(TestRig.Green), rig.At(TestRig.Blue));

    [Fact]
    public void StartFunction_LoopForward_StepsInOrderAndWraps()
    {
        var (rig, chaser) = RigWithChaser();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.5);
        Assert.Equal(((byte)255, (byte)0, (byte)0), Rgb(rig));

        rig.Tick(1);
        Assert.Equal(((byte)0, (byte)255, (byte)0), Rgb(rig)); // 1.5 s

        rig.Tick(1);
        Assert.Equal(((byte)255, (byte)0, (byte)0), Rgb(rig)); // 2.5 s: wrapped to step 1
    }

    [Fact]
    public void StartFunction_Backward_StartsAtLastStep()
    {
        var (rig, chaser) = RigWithChaser(direction: RunDirection.Backward);
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.5);
        Assert.Equal(((byte)0, (byte)255, (byte)0), Rgb(rig));

        rig.Tick(1);
        Assert.Equal(((byte)255, (byte)0, (byte)0), Rgb(rig));
    }

    [Fact]
    public void StartFunction_PingPong_ReturnsWithoutRepeatingEnds()
    {
        var (rig, chaser) = RigWithChaser(RunOrder.PingPong, steps: 3);
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        // Red, Green, Blue, Green, Red, …
        rig.Tick(2.5);
        Assert.Equal(((byte)0, (byte)0, (byte)255), Rgb(rig));

        rig.Tick(1);
        Assert.Equal(((byte)0, (byte)255, (byte)0), Rgb(rig)); // 3.5 s

        rig.Tick(1);
        Assert.Equal(((byte)255, (byte)0, (byte)0), Rgb(rig)); // 4.5 s
    }

    [Fact]
    public void StartFunction_SingleShot_ReleasesAfterLastStep()
    {
        var (rig, chaser) = RigWithChaser(RunOrder.SingleShot);
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(1.5);
        Assert.Equal(((byte)0, (byte)255, (byte)0), Rgb(rig));

        rig.Tick(1);
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void StartFunction_StepFadeOutAndNextFadeIn_CrossFades()
    {
        var (rig, chaser) = RigWithChaser();
        chaser.Steps[0].FadeOutSeconds = 1;
        chaser.Steps[1].FadeInSeconds = 1;
        chaser.Steps[1].HoldSeconds = 1;
        rig.RefreshAndApply();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(1.5);

        Assert.Equal(((byte)128, (byte)128, (byte)0), Rgb(rig)); // 127.5 each
    }

    [Fact]
    public void StartFunction_StepFadeIn_RampsFromBelow()
    {
        var (rig, chaser) = RigWithChaser();
        chaser.Steps[0].FadeInSeconds = 1;
        rig.RefreshAndApply();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(0.25);

        Assert.Equal(64, rig.At(TestRig.Red)); // 63.75
    }

    [Fact]
    public void StartFunction_StepWithMissingFunction_KeepsItsTimeDark()
    {
        var (rig, chaser) = RigWithChaser();
        chaser.Steps.Insert(1, new ChaserStep { FunctionId = Guid.NewGuid() });
        rig.RefreshAndApply();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(1.5);
        Assert.Equal(((byte)0, (byte)0, (byte)0), Rgb(rig));

        rig.Tick(1);
        Assert.Equal(((byte)0, (byte)255, (byte)0), Rgb(rig)); // 2.5 s: third step
    }

    [Fact]
    public void StartFunction_ChaserPlayingItself_SkipsTheLoopingStep()
    {
        var (rig, chaser) = RigWithChaser();
        chaser.AddStep(chaser.Id);
        rig.RefreshAndApply();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);

        rig.Tick(2.5);
        Assert.Equal(((byte)0, (byte)0, (byte)0), Rgb(rig)); // looping step: nothing

        rig.Tick(1);
        Assert.Equal(((byte)255, (byte)0, (byte)0), Rgb(rig)); // 3.5 s: wrapped
    }

    [Fact]
    public void StartFunction_NestedChaser_PlaysInnerStepsWithStepTime()
    {
        var (rig, inner) = RigWithChaser();
        var white = rig.Scene("White", ("w", 255));
        var outer = rig.Add(new ChaserFunction { Name = "Outer" });
        outer.AddStep(inner.Id, holdSeconds: 2);
        outer.AddStep(white.Id);
        rig.RefreshAndApply();
        rig.Engine.StartFunction(outer.Id, EngineLayer.Console);

        rig.Tick(1.5);
        Assert.Equal(255, rig.At(TestRig.Green)); // inner step 2

        rig.Tick(1);
        Assert.Equal(255, rig.At(TestRig.White)); // 2.5 s: outer step 2
        Assert.Equal(0, rig.At(TestRig.Green));

        rig.Tick(1);
        Assert.Equal(255, rig.At(TestRig.Red)); // 3.5 s: inner restarts at its step 1
    }

    [Fact]
    public void SetFunctionSpeed_Double_StepsTwiceAsFast()
    {
        var (rig, chaser) = RigWithChaser();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 2);

        rig.Tick(0.75); // function time 1.5 s

        Assert.Equal(((byte)0, (byte)255, (byte)0), Rgb(rig));
        Assert.Equal(2, Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions).Speed);
    }

    [Fact]
    public void SetFunctionSpeed_OutOfRange_IsClamped()
    {
        var (rig, chaser) = RigWithChaser();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 100);

        rig.Tick();

        Assert.Equal(DmxEngine.MaxSpeed, Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions).Speed);
    }

    [Fact]
    public void StartFunction_Restarted_ResetsSpeedOnlyForNewInstance()
    {
        var (rig, chaser) = RigWithChaser();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Engine.SetFunctionSpeed(chaser.Id, EngineLayer.Console, 0);
        rig.Tick(5);
        Assert.Equal(255, rig.At(TestRig.Red)); // frozen at function time 0

        rig.Engine.StopFunction(chaser.Id, EngineLayer.Console);
        rig.Tick();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(1.5);

        Assert.Equal(255, rig.At(TestRig.Green));
    }

    [Fact]
    public void StopFunction_Chaser_ReleasesAtOnce()
    {
        var (rig, chaser) = RigWithChaser();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(0.5);

        rig.Engine.StopFunction(chaser.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StartShow_ChaserItemAfterSeek_RendersTimeSinceItemStart()
    {
        var (rig, chaser) = RigWithChaser();
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 20;
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, chaser.Id, 2, 10);
        rig.RefreshAndApply();
        rig.Engine.StartShow(show.Id);
        rig.Engine.PauseShow(show.Id);

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(3.5)); // 1.5 s into the item
        rig.Tick();
        Assert.Equal(((byte)0, (byte)255, (byte)0), Rgb(rig));

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(4.5)); // 2.5 s into the item
        rig.Tick();
        Assert.Equal(((byte)255, (byte)0, (byte)0), Rgb(rig));
    }
}
