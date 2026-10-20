using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

public class SequenceAndFadePlaybackTests
{
    // ───────────────────────────── Sequence ─────────────────────────────

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

    // ───────────────────────────── Fade ─────────────────────────────

    [Fact]
    public void StartFunction_FadeBetweenScenes_InterpolatesSharedAndCrossFadesOthers()
    {
        var rig = new TestRig();
        var from = rig.Scene("From", ("r", 255), ("g", 100), ("macro", 0));
        var to = rig.Scene("To", ("r", 55), ("b", 200), ("macro", 100));
        var fade = rig.Add(new FadeFunction { Name = "Fade", FromSceneId = from.Id, ToSceneId = to.Id, DurationSeconds = 2 });
        rig.Load();
        rig.Engine.StartFunction(fade.Id, EngineLayer.Console);

        rig.Tick(1);
        Assert.Equal(155, rig.At(TestRig.Red)); // in both: 255 → 55
        Assert.Equal(50, rig.At(TestRig.Green)); // only in "from": fades out to the default 0
        Assert.Equal(100, rig.At(TestRig.Blue)); // only in "to": fades in from 0
        Assert.Equal(50, rig.At(TestRig.Macro)); // LTP interpolates too

        rig.Tick(1.5); // past the end: holds the target
        Assert.Equal(55, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green));
        Assert.Equal(200, rig.At(TestRig.Blue));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void StartFunction_FadeWithoutSource_FadesTargetInFromProfileDefault()
    {
        var rig = new TestRig();
        var to = rig.Scene("Dim off", ("dim", 0));
        var fade = rig.Add(new FadeFunction { Name = "Fade", ToSceneId = to.Id, DurationSeconds = 4 });
        rig.Load();
        rig.Engine.StartFunction(fade.Id, EngineLayer.Console);

        rig.Tick(1);

        Assert.Equal(191, rig.At(TestRig.Dimmer)); // 255 × 0.75
    }

    [Fact]
    public void StartFunction_FadeWithMissingScene_OutputsDefaults()
    {
        var rig = new TestRig();
        var fade = rig.Add(new FadeFunction { Name = "Fade", ToSceneId = Guid.NewGuid() });
        rig.Load();
        rig.Engine.StartFunction(fade.Id, EngineLayer.Console);

        rig.Tick(5);

        Assert.True(rig.Engine.CanPlay(fade.Id));
        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StartShow_FadeItemAfterSeek_UsesTimeSinceItemStart()
    {
        var rig = new TestRig();
        var to = rig.Scene("Red", ("r", 255));
        var fade = rig.Add(new FadeFunction { Name = "Fade", ToSceneId = to.Id, DurationSeconds = 4 });
        var show = rig.Project.CreateShow("Show");
        show.LengthSeconds = 20;
        rig.Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, fade.Id, 5, 10);
        rig.Load();
        rig.Engine.StartShow(show.Id);
        rig.Engine.PauseShow(show.Id);

        rig.Engine.SeekShow(show.Id, TimeSpan.FromSeconds(6));
        rig.Tick();

        Assert.Equal(64, rig.At(TestRig.Red)); // 1 s of 4: 63.75
    }
}
