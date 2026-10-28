using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

public class SequencePlaybackTests
{
    private static (TestRig Rig, SequenceFunction Sequence) RigWithSequence()
    {
        var rig = new TestRig();
        var sequence = rig.Add(new SequenceFunction { Name = "Seq", FixtureIds = [rig.Fixture.Id] });
        var first = new SequenceStep();
        first.SetValue(rig.Fixture.Id, "r", 200);
        first.SetValue(rig.Fixture.Id, "macro", 40);
        var second = new SequenceStep { FadeInSeconds = 1, HoldSeconds = 1 };
        second.SetValue(rig.Fixture.Id, "b", 100);
        second.SetValue(rig.Fixture.Id, "macro", 80);
        sequence.Steps.Add(first);
        sequence.Steps.Add(second);
        rig.Load();
        return (rig, sequence);
    }

    [Fact]
    public void StartFunction_Sequence_PlaysStepValuesInOrder()
    {
        var (rig, sequence) = RigWithSequence();
        rig.Engine.StartFunction(sequence.Id, EngineLayer.Console);

        rig.Tick(0.5);
        Assert.Equal(200, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Blue));
        Assert.Equal(40, rig.At(TestRig.Macro));

        rig.Tick(1); // 1.5 s: step 2 half way through its fade-in
        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(50, rig.At(TestRig.Blue));
        Assert.Equal(80, rig.At(TestRig.Macro)); // LTP: the newer step wins as soon as it plays

        rig.Tick(1); // 2.5 s
        Assert.Equal(100, rig.At(TestRig.Blue));

        rig.Tick(1); // 3.5 s: looped to step 1
        Assert.Equal(200, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Blue));
    }

    [Fact]
    public void StartFunction_SequenceSingleShotBackward_PlaysOnceFromLastStep()
    {
        var (rig, sequence) = RigWithSequence();
        sequence.RunOrder = RunOrder.SingleShot;
        sequence.Direction = RunDirection.Backward;
        rig.RefreshAndApply();
        rig.Engine.StartFunction(sequence.Id, EngineLayer.Console);

        rig.Tick(1.5);
        Assert.Equal(100, rig.At(TestRig.Blue));

        rig.Tick(1); // 2.5 s: step 1
        Assert.Equal(200, rig.At(TestRig.Red));

        rig.Tick(1); // 3.5 s: done
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }
}
