using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

public class FunctionCompileTests
{
    [Theory]
    [InlineData(FunctionType.Scene)]
    [InlineData(FunctionType.Chaser)]
    [InlineData(FunctionType.Sequence)]
    [InlineData(FunctionType.ColorCycle)]
    [InlineData(FunctionType.Fade)]
    [InlineData(FunctionType.Pulse)]
    [InlineData(FunctionType.Flash)]
    [InlineData(FunctionType.Strobe)]
    public void CanPlay_EveryFunctionType_IsPlayable(FunctionType type)
    {
        var rig = new TestRig();
        var function = rig.Add(FunctionFactory.Create(type));
        rig.Engine.LoadProject(rig.Project);

        Assert.True(rig.Engine.CanPlay(function.Id));
    }

    [Theory]
    [InlineData(FunctionType.Chaser)]
    [InlineData(FunctionType.Sequence)]
    [InlineData(FunctionType.ColorCycle)]
    [InlineData(FunctionType.Fade)]
    [InlineData(FunctionType.Pulse)]
    [InlineData(FunctionType.Flash)]
    [InlineData(FunctionType.Strobe)]
    public void StartFunction_EmptyNewFunction_OutputsDefaults(FunctionType type)
    {
        var rig = new TestRig();
        var function = rig.Add(FunctionFactory.Create(type));
        rig.Load();
        rig.Engine.StartFunction(function.Id, EngineLayer.Console);

        rig.Tick(1.3);

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void RefreshPatch_EditedChaser_RunningInstanceUsesNewSteps()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(red.Id);
        rig.Load();
        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick(0.5);

        chaser.Steps[0].FunctionId = green.Id;
        rig.RefreshAndApply();
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Equal(255, rig.At(TestRig.Green));
    }

    [Fact]
    public void StartFunction_TwoChasersPlayingEachOther_LoadsAndPlaysTheirOwnSteps()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var a = rig.Add(new ChaserFunction { Name = "A" });
        var b = rig.Add(new ChaserFunction { Name = "B" });
        a.AddStep(red.Id);
        a.AddStep(b.Id);
        b.AddStep(a.Id);
        rig.Load();

        rig.Engine.StartFunction(a.Id, EngineLayer.Console);
        rig.Tick(0.5);

        Assert.Equal(255, rig.At(TestRig.Red));
    }
}
