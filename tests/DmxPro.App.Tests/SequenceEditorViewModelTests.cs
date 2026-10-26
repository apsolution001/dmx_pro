using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>The sequence editor: steps (add / blank / duplicate / remove + undo / move), timing, colours, preview.</summary>
public sealed class SequenceEditorViewModelTests
{
    private readonly Project _project = new();
    private readonly Fixture _par1;
    private readonly Fixture _par2;

    public SequenceEditorViewModelTests()
    {
        var fixtures = _project.AddFixtures(Rgbw(), "6ch", 2, 1, 1, "PAR");
        _par1 = fixtures[0];
        _par2 = fixtures[1];
    }

    // ───────────────────────────── Opening ─────────────────────────────

    [Fact]
    public void Open_NoLights_StartsOnLightsTab()
    {
        var vm = Create(new SequenceFunction());

        Assert.Equal(SequenceEditorViewModel.LightsTab, vm.SelectedTabIndex);
        Assert.True(vm.HasNoLights);
        Assert.False(vm.AddStepCommand.CanExecute(null));
    }

    [Fact]
    public void Open_WithLightsAndSteps_StartsOnStepsWithFirstStepSelected()
    {
        var vm = Create(SequenceWithSteps(2));

        Assert.Equal(SequenceEditorViewModel.StepsTab, vm.SelectedTabIndex);
        Assert.Same(vm.Steps[0], vm.SelectedStep);
        Assert.Equal(2, vm.Lights.Count);
        Assert.False(vm.IsDirty);
    }

    // ───────────────────────────── Adding steps ─────────────────────────────

    [Fact]
    public void AddStep_NoSteps_AddsBlankStepWithDefaults()
    {
        var vm = Create(SequenceWithSteps(0));

        vm.AddStepCommand.Execute(null);

        var step = Assert.Single(vm.Steps).Step;
        Assert.Equal((byte)255, step.GetValue(_par1.Id, "dim"));
        Assert.Equal((byte)0, step.GetValue(_par2.Id, "r"));
        Assert.Same(vm.Steps[0], vm.SelectedStep);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void AddStep_CopiesSelectedStepRightAfterIt()
    {
        var sequence = SequenceWithSteps(3);
        sequence.Steps[0].SetValue(_par1.Id, "r", 200);
        sequence.Steps[0].HoldSeconds = 2.5;
        var vm = Create(sequence);

        vm.AddStepCommand.Execute(null);

        Assert.Equal(4, vm.Steps.Count);
        var copy = vm.Steps[1];
        Assert.Same(copy, vm.SelectedStep);
        Assert.Equal((byte)200, copy.Step.GetValue(_par1.Id, "r"));
        Assert.Equal(2.5, copy.Step.HoldSeconds);
        Assert.Null(copy.Step.Name);
        Assert.Equal(2, copy.Number);
    }

    [Fact]
    public void AddBlankStep_InsertsAfterSelectedWithItsTiming()
    {
        var sequence = SequenceWithSteps(2);
        sequence.Steps[0].SetValue(_par1.Id, "r", 200);
        sequence.Steps[0].FadeInSeconds = 0.5;
        var vm = Create(sequence);

        vm.AddBlankStepCommand.Execute(null);

        var blank = vm.Steps[1].Step;
        Assert.Equal((byte)0, blank.GetValue(_par1.Id, "r"));
        Assert.Equal(0.5, blank.FadeInSeconds);
        Assert.Equal(3, vm.Steps.Count);
    }

    [Fact]
    public void DuplicateSteps_SeveralSelected_CopiesAfterTheLastSelected()
    {
        var vm = Create(SequenceWithSteps(3));
        var first = vm.Steps[0].Step;
        var second = vm.Steps[1].Step;
        Select(vm, 0, 1);

        vm.DuplicateStepsCommand.Execute(null);

        Assert.Equal(5, vm.Steps.Count);
        Assert.Same(first, vm.Steps[0].Step);
        Assert.Same(second, vm.Steps[1].Step);
        Assert.True(vm.Steps[2].IsSelected && vm.Steps[3].IsSelected);
        Assert.False(vm.Steps[0].IsSelected);
    }

    // ───────────────────────────── Remove + undo ─────────────────────────────

    [Fact]
    public void RemoveSteps_ThenUndo_PutsThemBackInPlace()
    {
        var vm = Create(SequenceWithSteps(4));
        var original = vm.Steps.Select(r => r.Step).ToList();
        Select(vm, 1, 3);

        vm.RemoveStepsCommand.Execute(null);

        Assert.Equal(2, vm.Steps.Count);
        Assert.Equal("Removed 2 steps.", vm.UndoText);
        Assert.True(vm.UndoRemoveCommand.CanExecute(null));

        vm.UndoRemoveCommand.Execute(null);

        Assert.Equal(original, vm.Steps.Select(r => r.Step).ToList());
        Assert.Null(vm.UndoText);
    }

    [Fact]
    public void RemoveSteps_AnotherEdit_ClearsUndo()
    {
        var vm = Create(SequenceWithSteps(3));
        vm.RemoveStepsCommand.Execute(null);
        Assert.True(vm.HasUndo);

        vm.AddStepCommand.Execute(null);

        Assert.False(vm.HasUndo);
    }

    // ───────────────────────────── Moving ─────────────────────────────

    [Fact]
    public void MoveStepsDown_TwoSelected_MovesBothKeepingOrder()
    {
        var vm = Create(SequenceWithSteps(4));
        var (a, b, c, d) = (vm.Steps[0].Step, vm.Steps[1].Step, vm.Steps[2].Step, vm.Steps[3].Step);
        Select(vm, 0, 2);

        vm.MoveStepsDownCommand.Execute(null);

        Assert.Equal([b, a, d, c], vm.Steps.Select(r => r.Step).ToList());
        Assert.False(vm.MoveStepsDownCommand.CanExecute(null));
    }

    [Fact]
    public void MoveItems_AtTheEnd_DoesNothing()
    {
        var list = new List<string> { "a", "b", "c" };

        Assert.False(SequenceStepEditing.MoveItems(list, new HashSet<string> { "a" }, -1));
        Assert.True(SequenceStepEditing.MoveItems(list, new HashSet<string> { "b", "c" }, -1));
        Assert.Equal(["b", "c", "a"], list);
    }

    // ───────────────────────────── Timing ─────────────────────────────

    [Fact]
    public void ApplyTimingToSelected_OnlySelectedStepsChange()
    {
        var sequence = SequenceWithSteps(3);
        var vm = Create(sequence);
        Select(vm, 0, 2);
        vm.TimingFadeInText = "0.5";
        vm.TimingHoldText = "3";
        vm.TimingFadeOutText = "abc"; // invalid: keeps the previous value (0)

        vm.ApplyTimingToSelectedCommand.Execute(null);

        Assert.Equal(3, sequence.Steps.Count); // the original is untouched until Save
        Assert.Equal(0.5, vm.Steps[0].Step.FadeInSeconds);
        Assert.Equal(3, vm.Steps[2].Step.HoldSeconds);
        Assert.Equal(0, vm.Steps[2].Step.FadeOutSeconds);
        Assert.Equal(ChaserStep.DefaultHoldSeconds, vm.Steps[1].Step.HoldSeconds);
        Assert.Equal("3", vm.Steps[0].HoldText);
    }

    [Fact]
    public void ApplyTimingToAll_EveryStepAndCycleText()
    {
        var vm = Create(SequenceWithSteps(3));
        vm.TimingFadeInText = "1";
        vm.TimingHoldText = "2";

        vm.ApplyTimingToAllCommand.Execute(null);

        Assert.All(vm.Steps, r => Assert.Equal(2, r.Step.HoldSeconds));
        Assert.Contains("9", vm.CycleText);
    }

    [Fact]
    public void RunOrderRadios_SetRunOrder()
    {
        var vm = Create(SequenceWithSteps(1));

        vm.IsBackAndForth = true;
        vm.IsBackward = true;

        Assert.Equal(RunOrder.PingPong, vm.RunOrder);
        Assert.Equal(RunDirection.Backward, vm.Direction);
        Assert.False(vm.IsLoop);
        vm.Save();
        var saved = (SequenceFunction)vm.Original;
        Assert.Equal(RunOrder.PingPong, saved.RunOrder);
        Assert.Equal(RunDirection.Backward, saved.Direction);
    }

    // ───────────────────────────── Colours ─────────────────────────────

    [Fact]
    public void ApplyPreset_SelectedLightOnly_SetsColourAndFaders()
    {
        var vm = Create(SequenceWithSteps(1));
        vm.SelectNoLightsCommand.Execute(null);
        vm.Lights[1].IsSelected = true;
        var blue = SceneEditorColorPreset.All.First(p => p.Name == "Blue");

        vm.ApplyPresetCommand.Execute(blue);

        var step = vm.Steps[0].Step;
        Assert.Equal((byte)255, step.GetValue(_par2.Id, "b"));
        Assert.Equal((byte)0, step.GetValue(_par2.Id, "r"));
        Assert.Equal((byte)0, step.GetValue(_par1.Id, "b"));
        var fader = vm.Channels.Fixtures[1].Channels.First(f => f.Type == ChannelType.Blue);
        Assert.Equal(255, fader.ByteValue);
        Assert.Equal("#0000FF", vm.Lights[1].SwatchHex);
        Assert.Equal("#0000FF", vm.Steps[0].Swatches[1].Hex);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ApplyColor_DimmerAtZero_GoesToFull()
    {
        var step = new SequenceStep();
        var scene = step.AsScene();
        scene.AddFixture(_project, _par1);
        scene.SetChannelType(_project, _par1, ChannelType.Dimmer, 0);

        Assert.True(SequenceStepEditing.ApplyColor(scene, _project, _par1, SceneEditorColorPreset.All[0]));

        Assert.Equal((byte)255, step.GetValue(_par1.Id, "dim"));
        Assert.Equal((byte)255, step.GetValue(_par1.Id, "r"));
    }

    [Fact]
    public void LightsOff_SetsColoursAndIntensityToZero()
    {
        var sequence = SequenceWithSteps(1);
        sequence.Steps[0].SetValue(_par1.Id, "r", 255);
        var vm = Create(sequence);

        vm.LightsOffCommand.Execute(null);

        var step = vm.Steps[0].Step;
        Assert.Equal((byte)0, step.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)0, step.GetValue(_par1.Id, "dim"));
        Assert.Equal((byte)0, step.GetValue(_par2.Id, "dim"));
    }

    [Fact]
    public void Levels_OnlyTypesTheLightsHave_AndSettingOneWritesEverySelectedLight()
    {
        var vm = Create(SequenceWithSteps(1));

        Assert.Equal(
            [ChannelType.Dimmer, ChannelType.Red, ChannelType.Green, ChannelType.Blue, ChannelType.White],
            vm.Levels.Select(l => l.Type).ToList());

        vm.Levels.First(l => l.Type == ChannelType.Green).Value = 300;

        var step = vm.Steps[0].Step;
        Assert.Equal((byte)255, step.GetValue(_par1.Id, "g"));
        Assert.Equal((byte)255, step.GetValue(_par2.Id, "g"));
    }

    [Fact]
    public void LightSelection_KeptWhenAnotherStepIsSelected()
    {
        var vm = Create(SequenceWithSteps(2));
        vm.SelectNoLightsCommand.Execute(null);
        vm.Lights[1].IsSelected = true;

        vm.SelectedStep = vm.Steps[1];

        Assert.False(vm.Lights[0].IsSelected);
        Assert.True(vm.Lights[1].IsSelected);
    }

    // ───────────────────────────── Lights ─────────────────────────────

    [Fact]
    public void ChooseLights_AddsLightToEveryStep()
    {
        var sequence = new SequenceFunction { FixtureIds = [_par1.Id] };
        var vm = Create(sequence);
        vm.AddStepCommand.Execute(null);
        vm.AddStepCommand.Execute(null);
        vm.Fixtures.ShowPicker = picker =>
        {
            picker.SelectAllCommand.Execute(null);
            return true;
        };

        vm.Fixtures.ChooseLightsCommand.Execute(null);

        Assert.All(vm.Steps, r => Assert.Equal((byte)255, r.Step.GetValue(_par2.Id, "dim")));
        Assert.Equal(2, vm.Lights.Count);
        Assert.All(vm.Steps, r => Assert.Equal(2, r.Swatches.Count));
    }

    // ───────────────────────────── Preview / save ─────────────────────────────

    [Fact]
    public void BuildPreviewFunction_ThisStep_HoldsOnlyTheSelectedStep()
    {
        var sequence = SequenceWithSteps(3);
        sequence.RunOrder = RunOrder.SingleShot;
        sequence.Steps[1].SetValue(_par1.Id, "r", 123);
        var vm = Create(sequence);
        vm.SelectedStep = vm.Steps[1];

        var preview = (SequenceFunction)vm.BuildPreviewFunction();

        var step = Assert.Single(preview.Steps);
        Assert.Equal((byte)123, step.GetValue(_par1.Id, "r"));
        Assert.Equal(0, step.FadeInSeconds);
        Assert.Equal(FunctionEditing.MaxSeconds, step.HoldSeconds);
        Assert.Equal(RunOrder.Loop, preview.RunOrder);
        Assert.Equal(3, sequence.Steps.Count);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void BuildPreviewFunction_WholeSequence_PlaysAllSteps()
    {
        var sequence = SequenceWithSteps(3);
        sequence.RunOrder = RunOrder.SingleShot;
        var vm = Create(sequence);
        var changes = 0;
        vm.DraftChanged += (_, _) => changes++;

        vm.PreviewWholeSequence = true;

        Assert.Equal(1, changes); // the preview is re-sent ...
        Assert.False(vm.IsDirty); // ... without counting as an edit
        var preview = (SequenceFunction)vm.BuildPreviewFunction();
        Assert.Equal(3, preview.Steps.Count);
        Assert.Equal(RunOrder.SingleShot, preview.RunOrder);
    }

    [Fact]
    public void SelectingAStep_ResendsPreviewWithoutMarkingDirty()
    {
        var vm = Create(SequenceWithSteps(2));
        var changes = 0;
        vm.DraftChanged += (_, _) => changes++;

        vm.SelectedStep = vm.Steps[1];

        Assert.Equal(1, changes);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Save_WritesAllStepsNotThePreview()
    {
        var sequence = SequenceWithSteps(2);
        var vm = Create(sequence);
        vm.AddStepCommand.Execute(null);

        vm.Save();

        Assert.Equal(3, sequence.Steps.Count);
        Assert.Equal(ChaserStep.DefaultHoldSeconds, sequence.Steps[1].HoldSeconds);
    }

    [Fact]
    public void Swatches_CappedAndCounted()
    {
        var fixtures = _project.Fixtures.ToDictionary(f => f.Id);
        var step = new SequenceStep();
        step.SetValue(_par1.Id, "g", 255);

        var swatches = SequenceStepEditing.Swatches(step, [_par1.Id, Guid.NewGuid(), _par2.Id], fixtures, _project, max: 1);

        var swatch = Assert.Single(swatches);
        Assert.Equal("#00FF00", swatch.Hex);
        Assert.Contains("PAR 1", swatch.ToolTip);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private SequenceEditorViewModel Create(SequenceFunction sequence)
    {
        sequence.Name = "Seq";
        _project.Functions.Add(sequence);
        return new SequenceEditorViewModel(_project, sequence, new LightClipboardService());
    }

    private SequenceFunction SequenceWithSteps(int count)
    {
        var sequence = new SequenceFunction();
        sequence.AddSequenceFixture(_project, _par1);
        sequence.AddSequenceFixture(_project, _par2);
        for (var i = 0; i < count; i++)
        {
            sequence.AddSequenceStep(_project);
        }

        return sequence;
    }

    private static void Select(SequenceEditorViewModel vm, params int[] indexes)
    {
        foreach (var row in vm.Steps)
        {
            row.IsSelected = indexes.Contains(row.Number - 1);
        }

        vm.SelectedStep = vm.Steps[indexes[0]];
    }

    private static FixtureProfile Rgbw() => new()
    {
        Id = "test-rgbw-dim",
        Manufacturer = "Test",
        Model = "RGBW",
        Channels =
        [
            new() { Key = "r", Name = "Red", Type = ChannelType.Red },
            new() { Key = "g", Name = "Green", Type = ChannelType.Green },
            new() { Key = "b", Name = "Blue", Type = ChannelType.Blue },
            new() { Key = "w", Name = "White", Type = ChannelType.White },
            new() { Key = "dim", Name = "Dimmer", Type = ChannelType.Dimmer, Default = 255 },
            new() { Key = "strobe", Name = "Strobe", Type = ChannelType.Strobe },
        ],
        Modes =
        [
            new() { Name = "6ch", Channels = ["r", "g", "b", "w", "dim", "strobe"] },
        ],
    };
}
