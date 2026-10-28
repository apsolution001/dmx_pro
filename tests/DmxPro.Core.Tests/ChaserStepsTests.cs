using DmxPro.Core.Functions;

namespace DmxPro.Core.Tests;

public class ChaserStepsTests
{
    private readonly ChaserTestProject _t = new();

    [Fact]
    public void NewOwnLookScene_Values_HiddenOwnedSceneNotAddedToProject()
    {
        var chaserId = Guid.NewGuid();
        var value = _t.RedValue(10);

        var scene = ChaserSteps.NewOwnLookScene(chaserId, "Step 1", [value]);

        Assert.Equal("Step 1", scene.Name);
        Assert.Equal(chaserId, scene.OwnerFunctionId);
        Assert.Null(scene.OwnerShowId);
        Assert.True(scene.IsOwnedByFunction);
        Assert.True(scene.IsHiddenFromList);
        Assert.Equal((byte)10, scene.GetValue(_t.Fixture.Id, "r"));
        Assert.NotSame(value, scene.Values[0]);
        Assert.DoesNotContain(scene, _t.Project.Functions);
    }

    [Fact]
    public void CommitChaser_NewOwnLookStep_AddedHiddenAndOwned()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var draft = ChaserTestProject.Draft(chaser);
        var added = ChaserSteps.NewOwnLookScene(chaser.Id, "Step 2", [_t.RedValue(20)]);
        draft.AddStep(added.Id, holdSeconds: 2);
        draft.RunOrder = RunOrder.PingPong;
        draft.Name = "Chase 2";

        _t.Project.CommitChaser(draft, [added]);

        Assert.Same(chaser, _t.Project.FindFunction(chaser.Id));
        Assert.Equal("Chase 2", chaser.Name);
        Assert.Equal(RunOrder.PingPong, chaser.RunOrder);
        Assert.Equal(2, chaser.Steps.Count);
        Assert.Equal(2, chaser.Steps[1].HoldSeconds);
        // Stored as a copy: later edits to the editor's draft do not reach the project.
        var stored = Assert.IsType<SceneFunction>(_t.Project.FindFunction(added.Id));
        Assert.NotSame(added, stored);
        Assert.Equal((byte)20, stored.GetValue(_t.Fixture.Id, "r"));
        Assert.Equal(chaser.Id, stored.OwnerFunctionId);
        Assert.DoesNotContain(stored, _t.Project.FunctionListItems);
        added.SetValue(_t.Fixture.Id, "r", 1);
        Assert.Equal((byte)20, stored.GetValue(_t.Fixture.Id, "r"));
        Assert.NotSame(draft.Steps[0], chaser.Steps[0]);
        Assert.True(_t.Project.IsOwnLookStep(chaser.Steps[1], chaser.Id));
    }

    [Fact]
    public void CommitChaser_StepRemoved_OwnedFunctionDeleted()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10, 20);
        var removedId = chaser.Steps[0].FunctionId;
        var draft = ChaserTestProject.Draft(chaser);
        draft.Steps.RemoveAt(0);

        _t.Project.CommitChaser(draft, []);

        Assert.Null(_t.Project.FindFunction(removedId));
        Assert.Single(chaser.Steps);
        Assert.NotNull(_t.Project.FindFunction(chaser.Steps[0].FunctionId));
        Assert.Single(_t.Project.OwnedFunctions(chaser.Id));
    }

    [Fact]
    public void CommitChaser_ChangedOwnedScene_ReplacesSavedContent()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var draft = ChaserTestProject.Draft(chaser);
        var edited = (SceneFunction)_t.StepScene(chaser, 0).CloneAsNew();
        edited.Id = chaser.Steps[0].FunctionId;
        edited.SetValue(_t.Fixture.Id, "r", 99);

        _t.Project.CommitChaser(draft, [edited]);

        Assert.Equal((byte)99, _t.StepScene(chaser, 0).GetValue(_t.Fixture.Id, "r"));
        Assert.Single(_t.Project.OwnedFunctions(chaser.Id));
    }

    [Fact]
    public void CommitChaser_RemovedCopiedChaserStep_DeletesItsOwnedStepsToo()
    {
        var inner = _t.ChaserWithOwnLooks("Inner", 1, 2);
        var outer = _t.ChaserWithOwnLooks("Outer", 3);
        var draft = ChaserTestProject.Draft(outer);
        var copies = _t.Project.CopyForStep(outer.Id, inner);
        draft.AddStep(copies[0].Id);
        _t.Project.CommitChaser(draft, copies);
        Assert.Equal(4, _t.Project.OwnedFunctions(outer.Id).Count);

        draft = ChaserTestProject.Draft(outer);
        draft.Steps.RemoveAt(1);
        _t.Project.CommitChaser(draft, []);

        Assert.Single(_t.Project.OwnedFunctions(outer.Id));
        Assert.Equal(2, _t.Project.OwnedFunctions(inner.Id).Count);
    }

    [Fact]
    public void CommitChaser_OwnedByAnotherFunction_Throws()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var draft = ChaserTestProject.Draft(chaser);
        var foreign = ChaserSteps.NewOwnLookScene(Guid.NewGuid(), "Step", []);
        draft.AddStep(foreign.Id);

        Assert.Throws<ArgumentException>(() => _t.Project.CommitChaser(draft, [foreign]));
        Assert.Single(chaser.Steps);
        Assert.Null(_t.Project.FindFunction(foreign.Id));
    }

    [Fact]
    public void CommitChaser_UnknownChaser_Throws()
    {
        var draft = new ChaserFunction();

        Assert.Throws<ArgumentException>(() => _t.Project.CommitChaser(draft, []));
    }

    [Fact]
    public void CommitChaser_LegacyLibraryStep_LibraryFunctionKept()
    {
        var chaser = (ChaserFunction)_t.Project.CreateFunction(FunctionType.Chaser, "Old");
        chaser.AddStep(_t.Red.Id);
        var draft = ChaserTestProject.Draft(chaser);
        draft.Steps.Clear();

        _t.Project.CommitChaser(draft, []);

        Assert.Same(_t.Red, _t.Project.FindFunction(_t.Red.Id));
    }

    [Fact]
    public void CopyForStep_Scene_IsIndependentCopy()
    {
        var chaserId = Guid.NewGuid();
        var count = _t.Project.Functions.Count;

        var copies = _t.Project.CopyForStep(chaserId, _t.Red);

        var copy = Assert.IsType<SceneFunction>(Assert.Single(copies));
        Assert.NotEqual(_t.Red.Id, copy.Id);
        Assert.Equal(chaserId, copy.OwnerFunctionId);
        Assert.Null(copy.OwnerShowId);
        Assert.False(copy.ShowInFunctionList);
        Assert.Equal(_t.Red.Id, copy.CopiedFromId);
        Assert.Equal("Red", copy.Name);
        Assert.Equal(count, _t.Project.Functions.Count);

        copy.SetValue(_t.Fixture.Id, "r", 1);
        Assert.Equal((byte)255, _t.Red.GetValue(_t.Fixture.Id, "r"));
    }

    [Fact]
    public void CopyForStep_Chaser_CopiesItsOwnedSteps()
    {
        var inner = _t.ChaserWithOwnLooks("Inner", 1, 2);
        var outerId = Guid.NewGuid();

        var copies = _t.Project.CopyForStep(outerId, inner);

        Assert.Equal(3, copies.Count);
        var copy = Assert.IsType<ChaserFunction>(copies[0]);
        Assert.Equal(outerId, copy.OwnerFunctionId);
        for (var i = 0; i < 2; i++)
        {
            var stepCopy = Assert.IsType<SceneFunction>(copies.Single(f => f.Id == copy.Steps[i].FunctionId));
            Assert.Equal(copy.Id, stepCopy.OwnerFunctionId);
            Assert.NotEqual(inner.Steps[i].FunctionId, stepCopy.Id);
            Assert.Equal(_t.StepScene(inner, i).GetValue(_t.Fixture.Id, "r"), stepCopy.GetValue(_t.Fixture.Id, "r"));
        }
    }

    [Fact]
    public void CloneWithOwned_FadeOwnedByChaser_KeepsLibrarySceneReferences()
    {
        var fade = (FadeFunction)_t.Project.CreateFunction(FunctionType.Fade, "Fade");
        fade.ToSceneId = _t.Red.Id;

        var copy = Assert.IsType<FadeFunction>(Assert.Single(_t.Project.CloneWithOwned(fade)));

        Assert.Equal(_t.Red.Id, copy.ToSceneId);
        Assert.NotEqual(fade.Id, copy.Id);
    }

    [Fact]
    public void OwnedFunctions_Nested_AllInProjectOrder()
    {
        var inner = _t.ChaserWithOwnLooks("Inner", 1);
        var outer = _t.ChaserWithOwnLooks("Outer", 3);
        var draft = ChaserTestProject.Draft(outer);
        var copies = _t.Project.CopyForStep(outer.Id, inner);
        draft.AddStep(copies[0].Id);
        _t.Project.CommitChaser(draft, copies);

        var owned = _t.Project.OwnedFunctions(outer.Id);

        Assert.Equal(3, owned.Count);
        var expected = _t.Project.Functions.Where(owned.Contains).ToList();
        Assert.Equal(expected, owned);
        Assert.Contains(owned, f => f.Id == copies[1].Id);
    }

    [Fact]
    public void SaveStepAsFunction_CreatesLibraryFunction_StepKeepsCopy()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var stepScene = _t.StepScene(chaser, 0);

        var saved = Assert.IsType<SceneFunction>(_t.Project.SaveStepAsFunction(chaser.Id, 0, "  Warm  "));

        Assert.Equal("Warm", saved.Name);
        Assert.Null(saved.OwnerFunctionId);
        Assert.Null(saved.OwnerShowId);
        Assert.True(saved.ShowInFunctionList);
        Assert.Contains(saved, _t.Project.FunctionListItems);
        Assert.Equal(stepScene.Id, chaser.Steps[0].FunctionId);
        Assert.Same(stepScene, _t.Project.FindFunction(stepScene.Id));
        Assert.Equal((byte)10, saved.GetValue(_t.Fixture.Id, "r"));
        saved.SetValue(_t.Fixture.Id, "r", 1);
        Assert.Equal((byte)10, stepScene.GetValue(_t.Fixture.Id, "r"));
    }

    [Fact]
    public void SaveStepAsFunction_CopiedChaserStep_LibraryChaserGetsOwnSteps()
    {
        var inner = _t.ChaserWithOwnLooks("Inner", 1, 2);
        var outer = _t.ChaserWithOwnLooks("Outer", 3);
        var draft = ChaserTestProject.Draft(outer);
        var copies = _t.Project.CopyForStep(outer.Id, inner);
        draft.AddStep(copies[0].Id);
        _t.Project.CommitChaser(draft, copies);

        var saved = Assert.IsType<ChaserFunction>(_t.Project.SaveStepAsFunction(outer.Id, 1, "Inner 2"));

        var savedOwned = _t.Project.OwnedFunctions(saved.Id);
        Assert.Equal(2, savedOwned.Count);
        Assert.All(saved.Steps, s => Assert.Contains(savedOwned, f => f.Id == s.FunctionId));
        Assert.Equal(4, _t.Project.OwnedFunctions(outer.Id).Count);
    }

    [Fact]
    public void SaveStepAsFunction_BadIndex_Throws()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => _t.Project.SaveStepAsFunction(chaser.Id, 1, "X"));
    }

    [Fact]
    public void IsLinkedStep_LegacyLibraryReference_True()
    {
        var chaser = (ChaserFunction)_t.Project.CreateFunction(FunctionType.Chaser, "Old");
        var step = chaser.AddStep(_t.Red.Id);

        Assert.True(_t.Project.IsLinkedStep(step, chaser.Id));
        Assert.False(_t.Project.IsOwnLookStep(step, chaser.Id));
    }

    [Fact]
    public void IsOwnLookStep_OwnedSceneAndOwnedPulse_OnlySceneIsOwnLook()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var pulse = _t.Project.CreateFunction(FunctionType.Pulse, "Pulse");
        var draft = ChaserTestProject.Draft(chaser);
        var copies = _t.Project.CopyForStep(chaser.Id, pulse);
        draft.AddStep(copies[0].Id);
        _t.Project.CommitChaser(draft, copies);

        Assert.True(_t.Project.IsOwnLookStep(chaser.Steps[0], chaser.Id));
        Assert.False(_t.Project.IsLinkedStep(chaser.Steps[0], chaser.Id));
        Assert.False(_t.Project.IsOwnLookStep(chaser.Steps[1], chaser.Id));
        Assert.False(_t.Project.IsLinkedStep(chaser.Steps[1], chaser.Id));
    }

    [Fact]
    public void IsLinkedStep_MissingFunction_False()
    {
        var step = new ChaserStep { FunctionId = Guid.NewGuid() };

        Assert.False(_t.Project.IsLinkedStep(step, Guid.NewGuid()));
        Assert.False(_t.Project.IsOwnLookStep(step, Guid.NewGuid()));
    }
}
