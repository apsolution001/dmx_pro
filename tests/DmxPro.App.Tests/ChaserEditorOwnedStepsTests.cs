using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>
/// Chaser editor with own-look steps (owned hidden scenes edited in the chaser) and function steps (owned copies):
/// everything new stays in the editor draft until Save; linked library steps become own copies on their first edit.
/// </summary>
public sealed class ChaserEditorOwnedStepsTests : IDisposable
{
    private readonly AppRig _rig = new();
    private readonly Project _project = new();
    private readonly Fixture _par1;
    private readonly Fixture _par2;
    private readonly Fixture _par3;
    private readonly ChaserFunction _chaser;

    public ChaserEditorOwnedStepsTests()
    {
        var fixtures = _project.AddFixtures(Rgbw(), "6ch", 3, 1, 1, "PAR");
        _par1 = fixtures[0];
        _par2 = fixtures[1];
        _par3 = fixtures[2];
        _chaser = new ChaserFunction { Name = "Party" };
        _project.Functions.Add(_chaser);
    }

    public void Dispose() => _rig.Dispose();

    // ───────────────────────────── Adding ─────────────────────────────

    [Fact]
    public void AddStep_CopiesSelectedOwnLook_NewOwnedSceneInDraftOnly()
    {
        var look = AddOwnLook("Step 1", (_par1, "r", 200));
        var before = _project.Functions.Count;
        var vm = Create();
        vm.Steps[0].IsSelected = true;

        vm.AddStepCommand.Execute(null);

        Assert.Equal(2, vm.Steps.Count);
        var added = vm.Steps[1];
        Assert.True(added.IsOwnLook);
        Assert.True(added.IsSelected);
        var scene = Assert.IsType<SceneFunction>(added.Function);
        Assert.NotEqual(look.Id, scene.Id);
        Assert.Equal((byte)200, scene.GetValue(_par1.Id, "r"));
        Assert.Equal(_chaser.Id, scene.OwnerFunctionId);
        Assert.Equal(before, _project.Functions.Count);
        Assert.Null(_project.FindFunction(scene.Id));
        Assert.Single(_chaser.Steps);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void AddStep_AfterFunctionStep_StartsBlankWithNearestOwnLookLights()
    {
        AddOwnLook("Step 1", (_par1, "r", 255), (_par2, "g", 255));
        var pulse = new PulseFunction { Name = "Pulse", OwnerFunctionId = _chaser.Id, FixtureIds = [_par3.Id] };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Steps[1].IsSelected = true;

        vm.AddStepCommand.Execute(null);

        var scene = Assert.IsType<SceneFunction>(vm.Steps[2].Function);
        Assert.True(vm.Steps[2].IsOwnLook);
        Assert.Equal(new[] { _par1.Id, _par2.Id }.Order(), scene.TargetFixtureIds().Order());
        Assert.Equal((byte)0, scene.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)255, scene.GetValue(_par1.Id, "dim"));
    }

    [Fact]
    public void AddBlankStep_NoLooksAbove_HasNoLights()
    {
        var vm = Create();

        vm.AddBlankStepCommand.Execute(null);

        var scene = Assert.IsType<SceneFunction>(vm.Steps.Single().Function);
        Assert.Empty(scene.Values);
        Assert.True(vm.IsLookPanelVisible);
    }

    [Fact]
    public void AddFromFunction_Scene_BecomesOwnLookStep()
    {
        var red = AddLibraryScene("Red", (_par1, "r", 255));
        var vm = Create();

        vm.AddStepsCommand.Execute(vm.PickerItems.Single(p => p.Function == red));

        var row = vm.Steps.Single();
        Assert.True(row.IsOwnLook);
        Assert.NotEqual(red.Id, row.Function!.Id);
        Assert.Equal(red.Id, row.Function.CopiedFromId);
        Assert.Null(_project.FindFunction(row.Function.Id));

        vm.Save();

        var stored = Assert.IsType<SceneFunction>(_project.FindFunction(_chaser.Steps[0].FunctionId));
        Assert.True(stored.IsHiddenFromList);
        Assert.Equal(_chaser.Id, stored.OwnerFunctionId);
        Assert.DoesNotContain(stored, _project.FunctionListItems);
    }

    [Fact]
    public void AddFromFunction_Pulse_BecomesFunctionStepCopy()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        var vm = Create();

        vm.AddStepsCommand.Execute(vm.PickerItems.Single(p => p.Function == pulse));

        var row = vm.Steps.Single();
        Assert.True(row.IsFunctionStep);
        Assert.True(vm.IsFunctionPanelVisible);
        var copy = Assert.IsType<PulseFunction>(row.Function);
        Assert.NotEqual(pulse.Id, copy.Id);
        Assert.Equal(_chaser.Id, copy.OwnerFunctionId);
        Assert.Null(_project.FindFunction(copy.Id));
        Assert.True(vm.EditStepCommand.CanExecute(null));
    }

    // ───────────────────────────── Editing looks ─────────────────────────────

    [Fact]
    public void EditOwnLook_ChangesDraftScene_SaveCommitsAndOriginalLibraryUnchanged()
    {
        var red = AddLibraryScene("Red", (_par1, "r", 255));
        var vm = Create();
        vm.AddStepsCommand.Execute(vm.PickerItems.Single(p => p.Function == red));

        vm.StepLook!.ApplyPresetCommand.Execute(Preset("Blue"));

        Assert.Equal((byte)255, red.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)0, red.GetValue(_par1.Id, "b"));

        vm.Save();

        var stored = Assert.IsType<SceneFunction>(_project.FindFunction(_chaser.Steps[0].FunctionId));
        Assert.Equal((byte)255, stored.GetValue(_par1.Id, "b"));
        Assert.Equal((byte)0, stored.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)255, red.GetValue(_par1.Id, "r"));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void EditOwnLook_AfterSave_EditsDraftNotTheProjectObject()
    {
        var look = AddOwnLook("Step 1", (_par1, "r", 10));
        var vm = Create();
        vm.Steps[0].IsSelected = true;

        vm.StepLook!.ApplyPresetCommand.Execute(Preset("Green"));

        Assert.Equal((byte)10, look.GetValue(_par1.Id, "r")); // the saved scene waits for Save
        vm.Save();
        Assert.Equal((byte)255, ((SceneFunction)_project.FindFunction(look.Id)!).GetValue(_par1.Id, "g"));
    }

    [Fact]
    public void EditLinkedStep_FirstEdit_TurnsIntoOwnedCopy_NoteShown()
    {
        var red = AddLibraryScene("Red wash", (_par1, "r", 255));
        _chaser.AddStep(red.Id);
        var vm = Create();
        vm.Steps[0].IsSelected = true;

        Assert.True(vm.Steps[0].IsLinked);
        Assert.True(vm.HasLinkedHint);
        Assert.True(vm.IsLookPanelVisible);
        Assert.NotSame(red, vm.StepLook!.Scene);
        Assert.False(vm.IsDirty);

        vm.StepLook.ApplyPresetCommand.Execute(Preset("Blue"));

        Assert.True(vm.Steps[0].IsOwnLook);
        Assert.Contains("Red wash", vm.StepNote);
        Assert.Equal((byte)255, red.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)0, red.GetValue(_par1.Id, "b"));

        vm.Save();

        Assert.NotEqual(red.Id, _chaser.Steps[0].FunctionId);
        var own = Assert.IsType<SceneFunction>(_project.FindFunction(_chaser.Steps[0].FunctionId));
        Assert.Equal(_chaser.Id, own.OwnerFunctionId);
        Assert.Equal((byte)255, own.GetValue(_par1.Id, "b"));
        Assert.Contains(red, _project.FunctionListItems);
    }

    // ───────────────────────────── Remove / revert / save as ─────────────────────────────

    [Fact]
    public void RemoveStep_Save_DeletesOwnedFunction()
    {
        var look = AddOwnLook("Step 1", (_par1, "r", 1));
        var vm = Create();
        vm.Steps[0].IsSelected = true;

        vm.RemoveStepsCommand.Execute(null);

        Assert.Equal("Removed step 1.", vm.UndoText);
        Assert.NotNull(_project.FindFunction(look.Id));

        vm.Save();

        Assert.Null(_project.FindFunction(look.Id));
        Assert.Empty(_chaser.Steps);
    }

    [Fact]
    public void RemoveSteps_Undo_PutsThemBack()
    {
        AddOwnLook("A", (_par1, "r", 1));
        AddOwnLook("B", (_par1, "r", 2));
        AddOwnLook("C", (_par1, "r", 3));
        var vm = Create();
        vm.Steps[0].IsSelected = true;
        vm.Steps[2].IsSelected = true;

        vm.RemoveStepsCommand.Execute(null);
        Assert.Equal("Removed 2 steps.", vm.UndoText);
        vm.UndoRemoveCommand.Execute(null);

        Assert.Equal(["A", "B", "C"], vm.Steps.Select(s => s.FunctionName));
        Assert.Equal([true, false, true], vm.Steps.Select(s => s.IsSelected));
        Assert.False(vm.HasUndo);
    }

    [Fact]
    public void Revert_DiscardsNewOwnedSteps()
    {
        AddOwnLook("Step 1", (_par1, "r", 1));
        var before = _project.Functions.Select(f => f.Id).ToList();
        var vm = Create();
        vm.AddStepCommand.Execute(null);
        var newId = vm.Steps[1].Function!.Id;

        vm.RevertCommand.Execute(null);

        Assert.Single(vm.Steps);
        Assert.Equal(before, _project.Functions.Select(f => f.Id));
        Assert.DoesNotContain(vm.PreviewFunctions, f => f.Id == newId);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SaveStepAsFunction_AddsLibraryFunction()
    {
        var look = AddOwnLook("Step 1", (_par1, "r", 77));
        var vm = Create();
        vm.Steps[0].IsSelected = true;
        var saved = 0;
        vm.Saved += (_, _) => saved++;

        var copy = Assert.IsType<SceneFunction>(vm.SaveStepAsFunction("My look"));

        Assert.Equal("My look", copy.Name);
        Assert.Null(copy.OwnerFunctionId);
        Assert.Contains(copy, _project.FunctionListItems);
        Assert.Equal((byte)77, copy.GetValue(_par1.Id, "r"));
        Assert.Equal(look.Id, _chaser.Steps[0].FunctionId); // the step keeps its own copy
        Assert.Equal(1, saved);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void DuplicateSteps_OwnLook_GetsItsOwnCopy()
    {
        var look = AddOwnLook("Step 1", (_par1, "r", 5));
        var vm = Create();
        vm.Steps[0].IsSelected = true;

        vm.DuplicateStepsCommand.Execute(null);

        Assert.True(vm.Steps[1].IsOwnLook);
        Assert.NotEqual(look.Id, vm.Steps[1].Function!.Id);
        Assert.Equal("Step 2", vm.Steps[1].FunctionName);
    }

    // ───────────────────────────── Edit step ─────────────────────────────

    [Fact]
    public async Task EditStep_FunctionStep_UnsavedChaser_RequestsSave()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        _rig.Dialogs.SaveChangesAnswer = SaveChangesChoice.Save;
        vm.AddStepsCommand.Execute(vm.PickerItems.Single(p => p.Function == pulse));

        await vm.EditStepCommand.ExecuteAsync(null);

        Assert.Equal(["Party"], _rig.Dialogs.Prompts);
        var inner = Assert.IsType<PulseEditorViewModel>(vm.StepEditor);
        Assert.True(vm.IsInnerEditorOpen);
        Assert.Same(_project.FindFunction(_chaser.Steps[0].FunctionId), inner.Original);
        Assert.Equal(_chaser.Id, inner.Original.OwnerFunctionId);
        Assert.Contains("Party", vm.BackToChaserText);

        await vm.BackToChaserCommand.ExecuteAsync(null);

        Assert.Null(vm.StepEditor);
        Assert.False(vm.IsInnerEditorOpen);
        Assert.True(vm.Steps[0].IsSelected);
    }

    [Fact]
    public async Task EditStep_Cancel_StaysOnChaserWithChanges()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        _rig.Dialogs.SaveChangesAnswer = SaveChangesChoice.Cancel;
        vm.AddStepsCommand.Execute(vm.PickerItems.Single(p => p.Function == pulse));

        await vm.EditStepCommand.ExecuteAsync(null);

        Assert.Null(vm.StepEditor);
        Assert.True(vm.IsDirty);
        Assert.Single(vm.Steps);
    }

    [Fact]
    public async Task EditStep_DontSave_DropsNewStepAndOpensNothing()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        _rig.Dialogs.SaveChangesAnswer = SaveChangesChoice.Discard;
        vm.AddStepsCommand.Execute(vm.PickerItems.Single(p => p.Function == pulse));

        await vm.EditStepCommand.ExecuteAsync(null);

        Assert.Null(vm.StepEditor);
        Assert.Empty(vm.Steps);
        Assert.Empty(_project.OwnedFunctions(_chaser.Id));
    }

    [Fact]
    public async Task EditStep_LinkedFunctionStep_EditsAnOwnCopyNotTheLibrary()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        _rig.Dialogs.SaveChangesAnswer = SaveChangesChoice.Save;
        vm.Steps[0].IsSelected = true;

        await vm.EditStepCommand.ExecuteAsync(null);

        var inner = Assert.IsType<PulseEditorViewModel>(vm.StepEditor);
        Assert.NotSame(pulse, inner.Original);
        Assert.Equal(pulse.Id, inner.Original.CopiedFromId);
        Assert.NotEqual(pulse.Id, _chaser.Steps[0].FunctionId);
    }

    [Fact]
    public async Task InnerEditor_DirtyMirroredAndPreviewDelegated()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id], OwnerFunctionId = _chaser.Id };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        vm.Steps[0].IsSelected = true;

        await vm.EditStepCommand.ExecuteAsync(null);
        var inner = vm.StepEditor!;
        inner.Name = "Throb 2";

        Assert.True(vm.IsDirty);
        Assert.Equal("Throb 2", vm.BuildPreviewFunction().Name);

        vm.Save();

        Assert.Equal("Throb 2", _project.FindFunction(pulse.Id)!.Name);
        Assert.False(inner.IsDirty);
    }

    [Fact]
    public async Task EditStep_LinkedStep_Cancel_StaysLinkedAndDraftUnchanged()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        _rig.Dialogs.SaveChangesAnswer = SaveChangesChoice.Cancel;
        vm.Steps[0].IsSelected = true;
        vm.IsPlayOnce = true; // an unsaved chaser change, so Edit step has to ask

        await vm.EditStepCommand.ExecuteAsync(null);

        Assert.Equal(["Party"], _rig.Dialogs.Prompts);
        Assert.Null(vm.StepEditor);
        Assert.True(vm.Steps[0].IsLinked);
        Assert.Equal(pulse.Id, vm.Steps[0].Step.FunctionId);
        Assert.Null(vm.StepNote);
        Assert.DoesNotContain(vm.PreviewFunctions, f => f.CopiedFromId == pulse.Id);
        Assert.Equal(pulse.Id, _chaser.Steps[0].FunctionId);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task EditStep_LinkedStep_DirtyWithoutDialogs_DoesNothing()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Steps[0].IsSelected = true;
        vm.IsPlayOnce = true;

        await vm.EditStepCommand.ExecuteAsync(null);

        Assert.Null(vm.StepEditor);
        Assert.True(vm.Steps[0].IsLinked);
        Assert.Equal(pulse.Id, vm.Steps[0].Step.FunctionId);
        Assert.Empty(_project.OwnedFunctions(_chaser.Id));
    }

    [Fact]
    public async Task EditStep_LinkedStep_Clean_NoPromptCommitsOnlyTheCopy()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id] };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        vm.Steps[0].IsSelected = true;

        await vm.EditStepCommand.ExecuteAsync(null);

        Assert.Empty(_rig.Dialogs.Prompts);
        var inner = Assert.IsType<PulseEditorViewModel>(vm.StepEditor);
        Assert.Equal(pulse.Id, inner.Original.CopiedFromId);
        Assert.Equal(inner.Original.Id, _chaser.Steps[0].FunctionId);
        Assert.Contains(pulse, _project.FunctionListItems);
    }

    [Fact]
    public async Task Save_WhileStepEditorDirty_SavesBothAndRaisesSavedOnce()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id], OwnerFunctionId = _chaser.Id };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        vm.Steps[0].IsSelected = true;
        await vm.EditStepCommand.ExecuteAsync(null);
        var inner = vm.StepEditor!;
        inner.Name = "Throb 2";
        var saved = 0;
        vm.Saved += (_, _) => saved++;

        vm.Save();

        Assert.Equal(1, saved);
        Assert.Equal("Throb 2", _project.FindFunction(pulse.Id)!.Name);
        Assert.False(inner.IsDirty);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task StepEditorSave_OnItsOwn_RaisesChaserSaved()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id], OwnerFunctionId = _chaser.Id };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        vm.Steps[0].IsSelected = true;
        await vm.EditStepCommand.ExecuteAsync(null);
        var saved = 0;
        vm.Saved += (_, _) => saved++;
        vm.StepEditor!.Name = "Throb 2";

        vm.StepEditor.Save();

        Assert.Equal(1, saved);
    }

    [Theory]
    [InlineData(SaveChangesChoice.Save, "Throb 2", false)]
    [InlineData(SaveChangesChoice.Discard, "Throb", false)]
    [InlineData(SaveChangesChoice.Cancel, "Throb", true)]
    public async Task BackToChaser_DirtyStepEditor_FollowsTheAnswer(SaveChangesChoice answer, string storedName, bool staysOpen)
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id], OwnerFunctionId = _chaser.Id };
        _project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        var vm = Create();
        vm.Dialogs = _rig.Dialogs;
        vm.Steps[0].IsSelected = true;
        await vm.EditStepCommand.ExecuteAsync(null);
        var inner = vm.StepEditor!;
        inner.Name = "Throb 2";
        _rig.Dialogs.SaveChangesAnswer = answer;

        await vm.BackToChaserCommand.ExecuteAsync(null);

        Assert.Equal(["Throb"], _rig.Dialogs.Prompts);
        Assert.Equal(storedName, _project.FindFunction(pulse.Id)!.Name);
        if (staysOpen)
        {
            Assert.Same(inner, vm.StepEditor);
            Assert.True(vm.IsDirty);
            Assert.Equal("Throb 2", inner.Name);
        }
        else
        {
            Assert.Null(vm.StepEditor);
            Assert.False(vm.IsDirty);
            Assert.Equal(storedName, vm.Steps[0].FunctionName);
            Assert.True(vm.Steps[0].IsSelected);
        }
    }

    [Fact]
    public void IsStale_FreshEditor_False()
    {
        AddOwnLook("Step 1", (_par1, "r", 10));
        var vm = Create();

        Assert.False(vm.IsStale(_project));
    }

    [Fact]
    public void IsStale_OwnStepFunctionReplaced_True()
    {
        var look = AddOwnLook("Step 1", (_par1, "r", 10));
        var vm = Create();
        var replaced = look.CloneAsNew();
        replaced.Id = look.Id;
        _project.Functions[_project.Functions.IndexOf(look)] = replaced;

        Assert.True(vm.IsStale(_project));
    }

    [Fact]
    public void IsStale_AfterOwnSave_False()
    {
        AddOwnLook("Step 1", (_par1, "r", 10));
        var vm = Create();
        vm.Steps[0].IsSelected = true;
        vm.StepLook!.ApplyPresetCommand.Execute(Preset("Blue"));

        vm.Save(); // CommitChaser stores new objects for the changed step functions

        Assert.False(vm.IsStale(_project));
    }

    // ───────────────────────────── Preview ─────────────────────────────

    [Fact]
    public void BuildPreview_SelectedStep_HeldSingleStepWithDraftScene()
    {
        AddOwnLook("Step 1", (_par1, "r", 1));
        var vm = Create();
        vm.AddStepCommand.Execute(null); // new draft-only step, selected
        var newId = vm.Steps[1].Function!.Id;

        var preview = Assert.IsType<ChaserFunction>(vm.BuildPreviewFunction());

        var step = Assert.Single(preview.Steps);
        Assert.Equal(newId, step.FunctionId);
        Assert.Equal(0, step.FadeInSeconds);
        Assert.Equal(FunctionEditing.MaxSeconds, step.HoldSeconds);
        Assert.Contains(vm.PreviewFunctions, f => f.Id == newId);

        vm.PreviewWholeChaser = true;
        Assert.Equal(2, ((ChaserFunction)vm.BuildPreviewFunction()).Steps.Count);
        Assert.Single(_chaser.Steps);
    }

    // ───────────────────────────── Picker, timing, missing ─────────────────────────────

    [Fact]
    public void Picker_NoCandidatesVersusNoMatches()
    {
        var vm = Create();
        Assert.True(vm.HasNoCandidates);
        Assert.False(vm.HasNoMatches);

        AddLibraryScene("Red", (_par1, "r", 255));
        vm.IsPickerOpen = true;
        vm.PickerSearch = "zzz";

        Assert.False(vm.HasNoCandidates);
        Assert.True(vm.HasNoMatches);
        Assert.Equal("No matches for \"zzz\".", vm.NoMatchesText);
    }

    [Fact]
    public void Picker_HidesOwnedFunctions()
    {
        AddOwnLook("Step 1", (_par1, "r", 1));

        var vm = Create();

        Assert.DoesNotContain(vm.PickerItems, p => p.Function.IsHiddenFromList);
    }

    [Fact]
    public void SelectingStep_LoadsItsTimesIntoTheBoxes()
    {
        AddOwnLook("A", (_par1, "r", 1));
        var b = AddOwnLook("B", (_par1, "r", 2));
        _chaser.Steps[1].HoldSeconds = 4;
        _chaser.Steps[1].FadeInSeconds = 0.5;
        var vm = Create();

        vm.Steps[1].IsSelected = true;

        Assert.Equal(4, vm.NewHoldSeconds);
        Assert.Equal(0.5, vm.NewFadeInSeconds);
        Assert.Equal("Apply to selected (1)", vm.ApplyToSelectedText);
        Assert.Same(vm.Steps[1], vm.CurrentStep);
        Assert.Equal(b.Id, vm.StepLook!.Scene.Id);
    }

    [Fact]
    public void SyncSelection_FromList_SetsRowsAndCurrent()
    {
        AddOwnLook("A", (_par1, "r", 1));
        AddOwnLook("B", (_par1, "r", 2));
        var vm = Create();

        vm.SyncSelection([vm.Steps[0], vm.Steps[1]], vm.Steps[1]);

        Assert.Equal(2, vm.SelectedCount);
        Assert.Same(vm.Steps[1], vm.CurrentStep);
    }

    [Fact]
    public void DeletedFunction_ShowsDeletedAndPlaysNothing()
    {
        _chaser.AddStep(Guid.NewGuid());
        var vm = Create();
        vm.Steps[0].IsSelected = true;

        Assert.True(vm.Steps[0].IsMissing);
        Assert.Equal("(deleted function)", vm.Steps[0].FunctionName);
        Assert.Equal("Warning", vm.Steps[0].IconKind);
        Assert.True(vm.IsFunctionPanelVisible);
        Assert.False(vm.EditStepCommand.CanExecute(null));
    }

    // ───────────────────────────── Sequence fallback ─────────────────────────────

    [Fact]
    public void Factory_Sequence_OpensChaserEditorWithOwnLookSteps()
    {
        var sequence = new SequenceFunction { Name = "Seq", FixtureIds = [_par1.Id], RunOrder = RunOrder.PingPong };
        var step = new SequenceStep { Name = "Intro", HoldSeconds = 2 };
        step.SetValue(_par1.Id, "r", 99);
        sequence.Steps.Add(step);
        sequence.Steps.Add(new SequenceStep());
        _project.Functions.Add(sequence);

        var editor = FunctionEditorFactory.Create(_project, sequence, _rig.Settings, _rig.Clipboard);

        var vm = Assert.IsType<ChaserEditorViewModel>(editor);
        var chaser = Assert.IsType<ChaserFunction>(_project.FindFunction(sequence.Id));
        Assert.Same(chaser, vm.Original);
        Assert.Equal(RunOrder.PingPong, chaser.RunOrder);
        Assert.Equal(["Intro", "Step 2"], vm.Steps.Select(s => s.FunctionName));
        Assert.All(vm.Steps, s => Assert.True(s.IsOwnLook));
        Assert.Equal(2, vm.Steps[0].Step.HoldSeconds);
        Assert.DoesNotContain(_project.Functions, f => f is SequenceFunction);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private ChaserEditorViewModel Create() => new(_project, _chaser, _rig.Settings, _rig.Clipboard);

    private SceneFunction AddOwnLook(string name, params (Fixture Fixture, string Key, byte Value)[] values)
    {
        var scene = ChaserSteps.NewOwnLookScene(_chaser.Id, name, []);
        foreach (var fixture in values.Select(v => v.Fixture).Distinct())
        {
            scene.AddFixture(_project, fixture);
        }

        foreach (var (fixture, key, value) in values)
        {
            scene.SetValue(fixture.Id, key, value);
        }

        _project.Functions.Add(scene);
        _chaser.AddStep(scene.Id);
        return scene;
    }

    private SceneFunction AddLibraryScene(string name, params (Fixture Fixture, string Key, byte Value)[] values)
    {
        var scene = new SceneFunction { Name = name };
        foreach (var fixture in values.Select(v => v.Fixture).Distinct())
        {
            scene.AddFixture(_project, fixture);
        }

        foreach (var (fixture, key, value) in values)
        {
            scene.SetValue(fixture.Id, key, value);
        }

        _project.Functions.Add(scene);
        return scene;
    }

    private static SceneEditorColorPreset Preset(string name) => SceneEditorColorPreset.All.First(p => p.Name == name);

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
