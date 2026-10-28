using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>Editor helpers of the non-scene function types (<see cref="FunctionEditing"/>).</summary>
public class FunctionEditingTests
{
    private readonly Project _project = new();
    private readonly Fixture _par;
    private readonly Fixture _wash;

    public FunctionEditingTests()
    {
        var fixtures = _project.AddFixtures(TestProfiles.Rgbw(), "8ch", 2, 1, 1, "PAR");
        _par = fixtures[0];
        _wash = fixtures[1];
    }

    // ───────────────────────────── Number parsing ─────────────────────────────

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData(" 2 ", 2)]
    [InlineData("0.25 s", 0.25)]
    [InlineData("12 Hz", 12)]
    [InlineData("50 %", 50)]
    [InlineData("-3", 0)]
    [InlineData("9999", 100)]
    [InlineData("1.23456", 1.235)]
    public void TryParseNumber_ValidText_RoundsAndClamps(string text, double expected)
    {
        Assert.True(FunctionEditing.TryParseNumber(text, 0, 100, out var value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("∞")]
    public void TryParseNumber_InvalidText_ReturnsFalse(string? text)
    {
        Assert.False(FunctionEditing.TryParseNumber(text, 0, 100, out _));
    }

    [Fact]
    public void Clamp_NaN_ReturnsMin()
    {
        Assert.Equal(0.1, FunctionEditing.Clamp(double.NaN, 0.1, 10));
        Assert.Equal(10, FunctionEditing.Clamp(11, 0.1, 10));
    }

    // ───────────────────────────── Reordering ─────────────────────────────

    [Fact]
    public void MoveItem_UpAndDown_MovesAndStopsAtEnds()
    {
        var list = new List<string> { "a", "b", "c" };

        Assert.Equal(0, FunctionEditing.MoveItem(list, 1, -1));
        Assert.Equal(["b", "a", "c"], list);
        Assert.Equal(0, FunctionEditing.MoveItem(list, 0, -1));
        Assert.Equal(["b", "a", "c"], list);
        Assert.Equal(2, FunctionEditing.MoveItem(list, 0, 5));
        Assert.Equal(["a", "c", "b"], list);
        Assert.Equal(7, FunctionEditing.MoveItem(list, 7, 1));
    }

    // ───────────────────────────── Cycle time ─────────────────────────────

    [Fact]
    public void CycleSeconds_Loop_SumsFadeInAndHold()
    {
        var chaser = new ChaserFunction();
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 1, fadeInSeconds: 0.5, fadeOutSeconds: 2);
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 2, fadeInSeconds: 0, fadeOutSeconds: 0.5);

        Assert.Equal(3.5, chaser.CycleSeconds(), 6);
    }

    [Fact]
    public void CycleSeconds_SingleShot_IncludesLastFadeOut()
    {
        var chaser = new ChaserFunction { RunOrder = RunOrder.SingleShot };
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 1, fadeOutSeconds: 0);
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 1, fadeOutSeconds: 3);

        Assert.Equal(5, chaser.CycleSeconds(), 6);

        // Backward: the long fade-out now belongs to the first step played and still ends last (1 + 3).
        chaser.Direction = RunDirection.Backward;
        Assert.Equal(4, chaser.CycleSeconds(), 6);
    }

    [Fact]
    public void CycleSeconds_PingPong_PlaysMiddleStepsTwice()
    {
        var chaser = new ChaserFunction { RunOrder = RunOrder.PingPong };
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 1);
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 2);
        chaser.AddStep(Guid.NewGuid(), holdSeconds: 4);

        // 1 + 2 + 4, then back through the middle step only.
        Assert.Equal(9, chaser.CycleSeconds(), 6);
    }

    [Fact]
    public void CycleSeconds_EmptyOrZeroSteps_UsesMinimumStep()
    {
        Assert.Equal(0, new ChaserFunction().CycleSeconds());

        var sequence = new SequenceFunction();
        sequence.Steps.Add(new SequenceStep { HoldSeconds = 0 });
        sequence.Steps.Add(new SequenceStep { HoldSeconds = double.NaN, FadeInSeconds = -1 });

        Assert.Equal(2 * FunctionEditing.MinStepSeconds, sequence.CycleSeconds(), 6);
    }

    // ───────────────────────────── Loop-safe chaser steps ─────────────────────────────

    [Fact]
    public void CanPlayInChaser_SelfOrLoop_IsRejected()
    {
        var red = _project.CreateScene("Red");
        var outer = (ChaserFunction)_project.CreateFunction(FunctionType.Chaser, "Outer");
        var inner = (ChaserFunction)_project.CreateFunction(FunctionType.Chaser, "Inner");
        var deeper = (ChaserFunction)_project.CreateFunction(FunctionType.Chaser, "Deeper");
        inner.AddStep(deeper.Id);
        deeper.AddStep(outer.Id);

        Assert.True(_project.CanPlayInChaser(outer.Id, red));
        Assert.False(_project.CanPlayInChaser(outer.Id, outer));
        Assert.False(_project.CanPlayInChaser(outer.Id, deeper));
        Assert.False(_project.CanPlayInChaser(outer.Id, inner));
        Assert.True(_project.CanPlayInChaser(inner.Id, red));
    }

    [Fact]
    public void CanPlayInChaser_FadeUsingScenes_IsAllowed()
    {
        var red = _project.CreateScene("Red");
        var chaser = _project.CreateFunction(FunctionType.Chaser);
        var fade = new FadeFunction { Name = "Fade", ToSceneId = red.Id };
        _project.Functions.Add(fade);

        Assert.True(_project.CanPlayInChaser(chaser.Id, fade));
    }

    // ───────────────────────────── Sequence editing ─────────────────────────────

    [Fact]
    public void AddSequenceFixture_ExistingSteps_GetProfileDefaults()
    {
        var sequence = new SequenceFunction();
        var step = new SequenceStep();
        step.SetValue(_par.Id, "r", 200);
        sequence.Steps.Add(step);
        sequence.FixtureIds.Add(_par.Id);

        Assert.True(sequence.AddSequenceFixture(_project, _wash));
        Assert.False(sequence.AddSequenceFixture(_project, _wash));

        Assert.Equal([_par.Id, _wash.Id], sequence.FixtureIds);
        Assert.Equal((byte)255, step.GetValue(_wash.Id, "dim"));
        Assert.Equal((byte)0, step.GetValue(_wash.Id, "r"));
        Assert.Equal((byte)200, step.GetValue(_par.Id, "r"));
        Assert.Equal(9, step.Values.Count);
    }

    [Fact]
    public void RemoveSequenceFixture_RemovesIdAndStepValues()
    {
        var sequence = new SequenceFunction();
        sequence.AddSequenceFixture(_project, _par);
        sequence.AddSequenceFixture(_project, _wash);
        sequence.AddSequenceStep(_project);

        Assert.True(sequence.RemoveSequenceFixture(_par.Id));

        Assert.Equal([_wash.Id], sequence.FixtureIds);
        Assert.All(sequence.Steps[0].Values, v => Assert.Equal(_wash.Id, v.FixtureId));
        Assert.False(sequence.RemoveSequenceFixture(_par.Id));
    }

    [Fact]
    public void AddSequenceStep_New_HasEveryChannelAtDefault()
    {
        var sequence = new SequenceFunction();
        sequence.AddSequenceFixture(_project, _par);

        var step = sequence.AddSequenceStep(_project);

        Assert.Same(step, Assert.Single(sequence.Steps));
        Assert.Equal(8, step.Values.Count);
        Assert.Equal((byte)255, step.GetValue(_par.Id, "dim"));
    }

    [Fact]
    public void AddSequenceStep_Copy_InsertsIndependentCopyAfterSource()
    {
        var sequence = new SequenceFunction();
        sequence.AddSequenceFixture(_project, _par);
        var first = sequence.AddSequenceStep(_project);
        var last = sequence.AddSequenceStep(_project);
        first.Name = "Intro";
        first.HoldSeconds = 3;
        first.SetValue(_par.Id, "r", 99);

        var copy = sequence.AddSequenceStep(_project, first);

        Assert.Equal([first, copy, last], sequence.Steps);
        Assert.Null(copy.Name);
        Assert.Equal(3, copy.HoldSeconds);
        Assert.Equal((byte)99, copy.GetValue(_par.Id, "r"));
        copy.SetValue(_par.Id, "r", 1);
        Assert.Equal((byte)99, first.GetValue(_par.Id, "r"));
    }

    [Fact]
    public void AsScene_Edits_WriteThroughToStep()
    {
        var step = new SequenceStep();
        var scene = step.AsScene();

        scene.AddFixture(_project, _par);
        scene.SetChannelType(_project, _par, Profiles.ChannelType.Red, 128);
        scene.RemoveValue(_par.Id, "speed");

        Assert.Equal((byte)128, step.GetValue(_par.Id, "r"));
        Assert.Null(step.GetValue(_par.Id, "speed"));
        Assert.Equal(7, step.Values.Count);
    }
}
