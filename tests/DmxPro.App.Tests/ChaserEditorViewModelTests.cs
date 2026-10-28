using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

public sealed class ChaserEditorViewModelTests : IDisposable
{
    private readonly AppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void Load_ReadsStepsAndPlaybackWithoutDirtying()
    {
        var (project, chaser) = CreateProject("Red", "Green", "Blue");
        chaser.RunOrder = RunOrder.PingPong;
        chaser.Direction = RunDirection.Backward;

        var vm = Create(project, chaser);

        Assert.Equal(["Red", "Green", "Blue"], vm.Steps.Select(s => s.FunctionName));
        Assert.Equal([1, 2, 3], vm.Steps.Select(s => s.Number));
        Assert.True(vm.IsBackAndForth);
        Assert.True(vm.IsBackward);
        Assert.False(vm.IsDirty);
        Assert.Equal("3 steps", vm.StepCountText);
        Assert.Equal(new[] { 3, 2, 1, 2 }, vm.PlayOrderSteps.Select(s => s.Number));
    }

    [Fact]
    public void EmptyChaser_ShowsEmptyStateAndNoCycle()
    {
        var (project, chaser) = CreateProject();

        var vm = Create(project, chaser);

        Assert.True(vm.HasNoSteps);
        Assert.False(vm.ApplyTimingToAllCommand.CanExecute(null));
        Assert.Equal("No steps yet", vm.CycleText);
        Assert.Equal(ChaserStep.DefaultHoldSeconds, vm.NewHoldSeconds);
    }

    [Fact]
    public void AddSteps_InsertsAfterSelectionWithStepTimingAndSelectsThem()
    {
        var (project, chaser) = CreateProject("Red", "Green");
        var blue = AddScene(project, "Blue");
        var vm = Create(project, chaser);
        vm.Steps[0].IsSelected = true;
        vm.NewHoldSeconds = 2.5;
        vm.NewFadeInSeconds = 0.5;
        vm.IsPickerOpen = true;

        vm.AddStepsCommand.Execute(vm.PickerItems.Single(p => p.Function == blue));

        Assert.Equal(["Red", "Blue", "Green"], vm.Steps.Select(s => s.FunctionName));
        var added = vm.Steps[1];
        Assert.Equal(2.5, added.Step.HoldSeconds);
        Assert.Equal(0.5, added.Step.FadeInSeconds);
        Assert.True(added.IsSelected);
        Assert.False(vm.Steps[0].IsSelected);
        Assert.False(vm.IsPickerOpen);
        Assert.True(vm.IsDirty);
        Assert.Equal([1, 2, 3], vm.Steps.Select(s => s.Number));
    }

    [Fact]
    public void Picker_HidesTheChaserItselfAndFiltersBySearch()
    {
        var (project, chaser) = CreateProject("Red", "Green");
        var vm = Create(project, chaser);

        Assert.DoesNotContain(vm.PickerItems, p => p.Function == chaser);

        vm.PickerSearch = "gre";

        Assert.Equal(["Green"], vm.PickerItems.Select(p => p.Name));
    }

    [Fact]
    public void AddFirstMatch_AddsTheFirstSearchResult()
    {
        var (project, chaser) = CreateProject("Red", "Green");
        var vm = Create(project, chaser);
        vm.PickerSearch = "green";

        vm.AddFirstMatchCommand.Execute(null);

        Assert.Equal(["Red", "Green", "Green"], vm.Steps.Select(s => s.FunctionName));
    }

    [Fact]
    public void RemoveSteps_RemovesAllSelectedAndSelectsTheNext()
    {
        var (project, chaser) = CreateProject("A", "B", "C", "D");
        var vm = Create(project, chaser);
        vm.Steps[0].IsSelected = true;
        vm.Steps[2].IsSelected = true;

        vm.RemoveStepsCommand.Execute(null);

        Assert.Equal(["B", "D"], vm.Steps.Select(s => s.FunctionName));
        Assert.True(vm.Steps[0].IsSelected);
        Assert.Equal(1, vm.SelectedCount);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void DuplicateSteps_CopiesAfterLastSelected()
    {
        var (project, chaser) = CreateProject("A", "B", "C");
        var vm = Create(project, chaser);
        vm.Steps[0].IsSelected = true;
        vm.Steps[1].IsSelected = true;

        vm.DuplicateStepsCommand.Execute(null);

        Assert.Equal(["A", "B", "A", "B", "C"], vm.Steps.Select(s => s.FunctionName));
        Assert.Equal([false, false, true, true, false], vm.Steps.Select(s => s.IsSelected));
        Assert.NotSame(vm.Steps[0].Step, vm.Steps[2].Step);
    }

    [Fact]
    public void MoveSteps_UpDownAndCanExecuteAtTheEnds()
    {
        var (project, chaser) = CreateProject("A", "B", "C");
        var vm = Create(project, chaser);
        vm.Steps[0].IsSelected = true;

        Assert.False(vm.MoveStepsUpCommand.CanExecute(null));
        vm.MoveStepsDownCommand.Execute(null);

        Assert.Equal(["B", "A", "C"], vm.Steps.Select(s => s.FunctionName));
        Assert.Equal([1, 2, 3], vm.Steps.Select(s => s.Number));
        Assert.True(vm.Steps[1].IsSelected);
        Assert.True(vm.MoveStepsUpCommand.CanExecute(null));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void MoveStepsTo_DropsSelectionIntoGap()
    {
        var (project, chaser) = CreateProject("A", "B", "C", "D");
        var vm = Create(project, chaser);
        vm.Steps[3].IsSelected = true;

        vm.MoveStepsToCommand.Execute(1);

        Assert.Equal(["A", "D", "B", "C"], vm.Steps.Select(s => s.FunctionName));
    }

    [Fact]
    public void ApplyTimingToSelected_ChangesOnlySelectedSteps()
    {
        var (project, chaser) = CreateProject("A", "B", "C");
        var vm = Create(project, chaser);
        vm.Steps[1].IsSelected = true;
        vm.NewHoldSeconds = 3;
        vm.NewFadeOutSeconds = 1;

        Assert.Equal("Apply to selected (1)", vm.ApplyToSelectedText);
        vm.ApplyTimingToSelectedCommand.Execute(null);

        Assert.Equal([1.0, 3.0, 1.0], vm.Steps.Select(s => s.Step.HoldSeconds));
        Assert.Equal("3", vm.Steps[1].HoldText);
        Assert.Equal("1", vm.Steps[1].FadeOutText);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ApplyTimingToAll_UpdatesCycle()
    {
        var (project, chaser) = CreateProject("A", "B");
        var vm = Create(project, chaser);
        vm.NewHoldSeconds = 4;

        vm.ApplyTimingToAllCommand.Execute(null);

        Assert.Equal(EditorTiming.CycleText(2, 8, RunOrder.Loop), vm.CycleText);
    }

    [Fact]
    public void ApplyTiming_SameValues_StaysClean()
    {
        var (project, chaser) = CreateProject("A", "B");
        var vm = Create(project, chaser);

        vm.ApplyTimingToAllCommand.Execute(null);

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void StepTimingFields_ClampAndIgnoreEmptyWithoutDirtying()
    {
        var (project, chaser) = CreateProject("A");
        var vm = Create(project, chaser);

        vm.NewHoldSeconds = -5;
        Assert.Equal(0, vm.NewHoldSeconds);
        vm.NewHoldSeconds = 2;
        vm.NewHoldSeconds = null;
        Assert.Equal(2, vm.NewHoldSeconds);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void RunOrderRadios_SetRunOrderAndRebuildStrip()
    {
        var (project, chaser) = CreateProject("A", "B", "C");
        var vm = Create(project, chaser);

        vm.IsBackAndForth = true;
        Assert.Equal(RunOrder.PingPong, vm.RunOrder);
        Assert.Equal(new[] { 1, 2, 3, 2 }, vm.PlayOrderSteps.Select(s => s.Number));

        vm.IsPlayOnce = true;
        vm.IsBackward = true;
        Assert.Equal("then stops", vm.EndOfCycleText);
        Assert.Equal(new[] { 3, 2, 1 }, vm.PlayOrderSteps.Select(s => s.Number));

        // Unchecking a radio (WPF writes false to the old one) changes nothing.
        vm.IsLoop = false;
        Assert.Equal(RunOrder.SingleShot, vm.RunOrder);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Save_WritesStepsAndPlayback_RevertRestores()
    {
        var (project, chaser) = CreateProject("A", "B");
        var vm = Create(project, chaser);
        vm.Steps[0].IsSelected = true;
        vm.MoveStepsDownCommand.Execute(null);
        vm.IsBackAndForth = true;

        vm.Save();

        Assert.Equal(["B", "A"], chaser.Steps.Select(s => project.FindFunction(s.FunctionId)!.Name));
        Assert.Equal(RunOrder.PingPong, chaser.RunOrder);

        vm.RemoveStepsCommand.Execute(null);
        vm.RevertCommand.Execute(null);

        Assert.Equal(2, vm.Steps.Count);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SelectionCount_FollowsRows()
    {
        var (project, chaser) = CreateProject("A", "B", "C");
        var vm = Create(project, chaser);

        vm.SelectAllStepsCommand.Execute(null);

        Assert.Equal(3, vm.SelectedCount);
        Assert.Equal("3 of 3 selected", vm.StepCountText);
        Assert.True(vm.RemoveStepsCommand.CanExecute(null));
    }

    [Fact]
    public void MissingFunction_ShowsAsMissing()
    {
        var (project, chaser) = CreateProject("A");
        chaser.AddStep(Guid.NewGuid());

        var vm = Create(project, chaser);

        Assert.True(vm.Steps[1].IsMissing);
        Assert.Equal("(deleted function)", vm.Steps[1].FunctionName);
        Assert.False(vm.Steps[1].HasColor);
    }

    private ChaserEditorViewModel Create(Project project, ChaserFunction chaser) => new(project, chaser, _rig.Settings, _rig.Clipboard);

    private static (Project Project, ChaserFunction Chaser) CreateProject(params string[] sceneNames)
    {
        var project = new Project();
        var chaser = new ChaserFunction { Name = "Party" };
        project.Functions.Add(chaser);
        foreach (var name in sceneNames)
        {
            chaser.AddStep(AddScene(project, name).Id);
        }

        return (project, chaser);
    }

    private static SceneFunction AddScene(Project project, string name)
    {
        var scene = new SceneFunction { Name = name };
        project.Functions.Add(scene);
        return scene;
    }
}
