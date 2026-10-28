using DmxPro.Core.Functions;
using DmxPro.Core.History;
using DmxPro.Core.Projects;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Tests;

public class OwnedFunctionLifecycleTests
{
    private readonly ChaserTestProject _t = new();

    private Project Project => _t.Project;

    /// <summary>Outer chaser: own look (red 3) + a copied step of <c>inner</c> (two own looks, red 1 and 2).</summary>
    private (ChaserFunction Inner, ChaserFunction Outer) NestedChasers()
    {
        var inner = _t.ChaserWithOwnLooks("Inner", 1, 2);
        var outer = _t.ChaserWithOwnLooks("Outer", 3);
        var draft = ChaserTestProject.Draft(outer);
        var copies = Project.CopyForStep(outer.Id, inner);
        draft.AddStep(copies[0].Id);
        Project.CommitChaser(draft, copies);
        return (inner, outer);
    }

    /// <summary>Every step of <paramref name="chaser"/> (recursively) points at a function owned by its chaser.</summary>
    private void AssertStepsOwned(ChaserFunction chaser)
    {
        foreach (var step in chaser.Steps)
        {
            var function = Project.FindFunction(step.FunctionId);
            Assert.NotNull(function);
            Assert.Equal(chaser.Id, function.OwnerFunctionId);
            if (function is ChaserFunction nested)
            {
                AssertStepsOwned(nested);
            }
        }
    }

    [Fact]
    public void FunctionListItems_OwnedByChaser_Hidden()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10, 20);
        var owned = Project.OwnedFunctions(chaser.Id);

        Assert.Equal([_t.Red, chaser], Project.FunctionListItems.ToList());
        Assert.Equal(2, owned.Count);
        Assert.All(owned, f => Assert.True(f.IsHiddenFromList));
        Assert.False(chaser.IsHiddenFromList);
    }

    [Fact]
    public void FunctionListItems_OwnedEvenWithShowInFunctionList_Hidden()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var owned = _t.StepScene(chaser, 0);
        owned.ShowInFunctionList = true;

        Assert.DoesNotContain(owned, Project.FunctionListItems);
    }

    [Fact]
    public void IsHiddenFromList_ShowCopies_SameAsBefore()
    {
        var listed = new SceneFunction { OwnerShowId = Guid.NewGuid(), ShowInFunctionList = true };
        var hidden = new SceneFunction { OwnerShowId = Guid.NewGuid(), ShowInFunctionList = false };
        var library = new SceneFunction { ShowInFunctionList = false };

        Assert.False(listed.IsHiddenFromList);
        Assert.True(hidden.IsHiddenFromList);
        Assert.False(library.IsHiddenFromList);
    }

    [Fact]
    public void CloneAsNew_OwnedFunction_KeepsOwnerFunctionId()
    {
        var scene = ChaserSteps.NewOwnLookScene(Guid.NewGuid(), "Step 1", []);

        Assert.Equal(scene.OwnerFunctionId, scene.CloneAsNew().OwnerFunctionId);
    }

    [Fact]
    public void DeleteFunction_Chaser_DeletesOwnedRecursively()
    {
        var (inner, outer) = NestedChasers();
        var innerOwned = Project.OwnedFunctions(inner.Id);

        Assert.Equal(FunctionDeleteResult.Deleted, Project.DeleteFunction(outer.Id));

        var expected = new List<LightFunction> { _t.Red, inner };
        expected.AddRange(innerOwned);
        Assert.Equal(expected.OrderBy(f => f.Id), Project.Functions.OrderBy(f => f.Id));
    }

    [Fact]
    public void DeleteFunction_ShowCopyStillUsed_KeepsOwnedSteps()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var show = Project.CreateShow("Show");
        var item = Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, chaser.Id, 0, 4);
        var copy = Project.FindFunction(item.FunctionId)!;
        copy.ShowInFunctionList = true;

        Assert.Equal(FunctionDeleteResult.HiddenFromList, Project.DeleteFunction(copy.Id));

        Assert.Single(Project.OwnedFunctions(copy.Id));
    }

    [Fact]
    public void DuplicateFunction_Chaser_DeepCopiesOwnedSteps()
    {
        var (_, outer) = NestedChasers();
        var count = Project.Functions.Count;

        var duplicate = Assert.IsType<ChaserFunction>(Project.DuplicateFunction(outer.Id));

        Assert.Equal(count + 5, Project.Functions.Count);
        Assert.Null(duplicate.OwnerFunctionId);
        Assert.Contains(duplicate, Project.FunctionListItems);
        AssertStepsOwned(duplicate);
        AssertStepsOwned(outer);

        Assert.IsType<SceneFunction>(Project.FindFunction(duplicate.Steps[0].FunctionId)).SetValue(_t.Fixture.Id, "r", 200);
        Assert.Equal((byte)3, _t.StepScene(outer, 0).GetValue(_t.Fixture.Id, "r"));
    }

    [Fact]
    public void DuplicateFunction_OwnedStep_BecomesLibraryFunction()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);

        var copy = Project.DuplicateFunction(chaser.Steps[0].FunctionId, "Free");

        Assert.Null(copy.OwnerFunctionId);
        Assert.Contains(copy, Project.FunctionListItems);
    }

    [Fact]
    public void ClipboardPaste_Chaser_DeepCopiesOwnedSteps()
    {
        var (_, outer) = NestedChasers();
        var clipboard = new FunctionClipboard();
        clipboard.Copy(outer, Project);

        // Cut: the original and its owned steps are gone before the paste.
        Project.DeleteFunction(outer.Id);
        var first = Assert.IsType<ChaserFunction>(clipboard.PasteInto(Project));
        var second = Assert.IsType<ChaserFunction>(clipboard.PasteInto(Project));

        AssertStepsOwned(first);
        AssertStepsOwned(second);
        Assert.Null(first.OwnerFunctionId);
        Assert.Contains(first, Project.FunctionListItems);
        Assert.Equal((byte)3, _t.StepScene(first, 0).GetValue(_t.Fixture.Id, "r"));
        Assert.NotEqual(first.Steps[0].FunctionId, second.Steps[0].FunctionId);
        Assert.Equal(4, Project.OwnedFunctions(first.Id).Count);
        Assert.Equal(4, Project.OwnedFunctions(second.Id).Count);
    }

    [Fact]
    public void ClipboardPaste_ChaserCopiedWithoutProject_StillDeepCopiesOwnedSteps()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var clipboard = new FunctionClipboard();
        clipboard.Copy(chaser);

        var pasted = Assert.IsType<ChaserFunction>(clipboard.PasteInto(Project));

        AssertStepsOwned(pasted);
        AssertStepsOwned(chaser);
        Assert.NotEqual(chaser.Steps[0].FunctionId, pasted.Steps[0].FunctionId);
    }

    [Fact]
    public void ClipboardCopy_LaterEditsToOwnedStep_NotPasted()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var clipboard = new FunctionClipboard();
        clipboard.Copy(chaser, Project);
        _t.StepScene(chaser, 0).SetValue(_t.Fixture.Id, "r", 99);

        var pasted = Assert.IsType<ChaserFunction>(clipboard.PasteInto(Project));

        Assert.Equal((byte)10, _t.StepScene(pasted, 0).GetValue(_t.Fixture.Id, "r"));
    }

    [Fact]
    public void PlaceFunctionInShow_Chaser_CopiesOwnedSteps_ThenDeleteShow_LeavesNoOrphans()
    {
        var (_, outer) = NestedChasers();
        var before = Project.Functions.ToList();
        var show = Project.CreateShow("Show");

        var item = Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, outer.Id, 0, 4);

        var copy = Assert.IsType<ChaserFunction>(Project.FindFunction(item.FunctionId));
        Assert.Equal(show.Id, copy.OwnerShowId);
        Assert.Null(copy.OwnerFunctionId);
        AssertStepsOwned(copy);
        Assert.Equal(4, Project.OwnedFunctions(copy.Id).Count);
        Assert.All(Project.OwnedFunctions(copy.Id), f => Assert.Null(f.OwnerShowId));

        Assert.True(Project.DeleteShow(show.Id));

        Assert.Equal(before, Project.Functions);
    }

    [Fact]
    public void DeleteShow_ListedChaserCopy_KeepsItsOwnedSteps()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var show = Project.CreateShow("Show");
        var item = Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, chaser.Id, 0, 4);
        Project.SetShowInFunctionList(item.FunctionId, true);

        Project.DeleteShow(show.Id);

        var kept = Assert.IsType<ChaserFunction>(Project.FindFunction(item.FunctionId));
        Assert.Null(kept.OwnerShowId);
        AssertStepsOwned(kept);
    }

    [Fact]
    public void DuplicateShowItem_Chaser_CopyHasOwnSteps_RemoveItemCleansUp()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var show = Project.CreateShow("Show");
        var item = Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, chaser.Id, 0, 4);
        var before = Project.Functions.ToList();

        var duplicate = Project.DuplicateShowItem(show.Id, item.Id);

        var copy = Assert.IsType<ChaserFunction>(Project.FindFunction(duplicate.FunctionId));
        AssertStepsOwned(copy);
        Assert.NotEqual(((ChaserFunction)Project.FindFunction(item.FunctionId)!).Steps[0].FunctionId, copy.Steps[0].FunctionId);

        Assert.True(Project.RemoveShowItem(show.Id, duplicate.Id));
        Assert.Equal(before, Project.Functions);
    }

    [Fact]
    public void RemoveTrack_ChaserCopy_RemovesOwnedSteps()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var before = Project.Functions.ToList();
        var show = Project.CreateShow("Show");
        Project.PlaceFunctionInShow(show.Id, show.Tracks[0].Id, chaser.Id, 0, 4);

        Assert.True(Project.RemoveTrack(show.Id, show.Tracks[0].Id));

        Assert.Equal(before, Project.Functions);
    }

    [Fact]
    public void VirtualConsoleTargets_ExcludeOwned()
    {
        var (inner, outer) = NestedChasers();

        var page = Project.CreateDefaultConsolePage();

        var targets = page.Widgets.OfType<FunctionWidget>().Select(w => w.FunctionId).ToList();
        Assert.Equal([_t.Red.Id, inner.Id, outer.Id], targets);
    }

    [Fact]
    public void ProjectHistory_UndoAfterCommitChaser_RestoresChaserAndOwned()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10, 20);
        var history = new ProjectHistory();
        history.Reset(Project);
        var before = HistoryTestProjects.Json(Project);

        var draft = ChaserTestProject.Draft(chaser);
        draft.Steps.RemoveAt(0);
        var added = ChaserSteps.NewOwnLookScene(chaser.Id, "Step 3", [_t.RedValue(30)]);
        draft.AddStep(added.Id);
        Project.CommitChaser(draft, [added]);
        history.NoteChange(TimeSpan.Zero);
        history.Capture(Project);
        var after = HistoryTestProjects.Json(Project);
        Assert.NotEqual(before, after);

        Assert.NotNull(history.Undo(Project));
        Assert.Equal(before, HistoryTestProjects.Json(Project));
        Assert.Equal(2, Project.OwnedFunctions(chaser.Id).Count);
        Assert.Null(Project.FindFunction(added.Id));
        AssertStepsOwned((ChaserFunction)Project.FindFunction(chaser.Id)!);

        Assert.NotNull(history.Redo(Project));
        Assert.Equal(after, HistoryTestProjects.Json(Project));
    }

    [Fact]
    public void RoundTrip_OwnerFunctionId_Serialized()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var serializer = new ProjectSerializer();

        var json = serializer.Serialize(Project);
        var loaded = serializer.Deserialize(json).Project;

        Assert.Contains("\"ownerFunctionId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isOwnedByFunction", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isHiddenFromList", json, StringComparison.OrdinalIgnoreCase);
        var step = loaded.FindFunction(chaser.Steps[0].FunctionId)!;
        Assert.Equal(chaser.Id, step.OwnerFunctionId);
        Assert.Null(loaded.FindFunction(_t.Red.Id)!.OwnerFunctionId);
        Assert.Equal(json, serializer.Serialize(loaded));
    }

    [Fact]
    public void ProjectSnapshot_OwnerFunctionId_CapturedAndRestored()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var stepId = chaser.Steps[0].FunctionId;
        var snapshot = ProjectSnapshot.Capture(Project);

        Project.FindFunction(stepId)!.OwnerFunctionId = null;
        Assert.False(ProjectSnapshot.Capture(Project).ContentEquals(snapshot));

        var restored = snapshot.ToProject();
        Assert.Equal(chaser.Id, restored.FindFunction(stepId)!.OwnerFunctionId);
    }

    [Fact]
    public void FunctionsReferencing_LibrarySceneUsedByOwnedFadeStep_ReportsOuterChaser()
    {
        var (_, outer) = NestedChasers();
        var fade = (FadeFunction)Project.CreateFunction(FunctionType.Fade, "Fade");
        fade.FromSceneId = _t.Red.Id;
        var draft = ChaserTestProject.Draft(outer);
        var copies = Project.CopyForStep(outer.Id, fade);
        draft.AddStep(copies[0].Id);
        Project.CommitChaser(draft, copies);

        var users = Project.FunctionsReferencing(_t.Red.Id);

        Assert.Equal([fade, outer], users);
    }

    [Fact]
    public void FunctionsReferencing_OwnedStep_ReportsOwnerOnce()
    {
        var (inner, outer) = NestedChasers();
        var copiedInner = Project.FindFunction(outer.Steps[1].FunctionId)!;

        Assert.Equal([outer], Project.FunctionsReferencing(copiedInner.Id));
        Assert.Equal([inner], Project.FunctionsReferencing(inner.Steps[0].FunctionId));
        Assert.Equal([outer], Project.FunctionsReferencing(((ChaserFunction)copiedInner).Steps[0].FunctionId));
    }

    [Fact]
    public void Load_OldFileWithoutOwnerFunctionId_LibraryFunctionsUnowned()
    {
        var serializer = new ProjectSerializer();
        var chaser = (ChaserFunction)Project.CreateFunction(FunctionType.Chaser, "Old");
        chaser.AddStep(_t.Red.Id);

        var loaded = serializer.Deserialize(serializer.Serialize(Project)).Project;

        Assert.All(loaded.Functions, f => Assert.Null(f.OwnerFunctionId));
        Assert.Equal(2, loaded.FunctionListItems.Count());
    }

    [Fact]
    public void Validate_OwnedFunctionWithoutOwner_Warns()
    {
        var orphan = ChaserSteps.NewOwnLookScene(Guid.NewGuid(), "Lost", []);
        Project.Functions.Add(orphan);

        Assert.Contains(Project.ValidateFunctionReferences(), i => i.Code == "FUNCTION_OWNER_MISSING" && i.EntityId == orphan.Id);
    }

    [Fact]
    public void Validate_TwoOwners_Warns()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var step = _t.StepScene(chaser, 0);
        step.OwnerShowId = Guid.NewGuid();

        Assert.Contains(Project.ValidateFunctionReferences(), i => i.Code == "FUNCTION_TWO_OWNERS" && i.EntityId == step.Id);
    }

    [Fact]
    public void Validate_OwnedStepUsedByAnotherChaser_Warns()
    {
        var chaser = _t.ChaserWithOwnLooks("Chase", 10);
        var other = (ChaserFunction)Project.CreateFunction(FunctionType.Chaser, "Other");
        other.AddStep(chaser.Steps[0].FunctionId);

        Assert.Contains(Project.ValidateFunctionReferences(), i => i.Code == "FUNCTION_OWNED_SHARED" && i.EntityId == other.Id);
    }

    [Fact]
    public void Validate_CleanOwnedSteps_NoOwnershipWarnings()
    {
        NestedChasers();

        Assert.Empty(Project.ValidateFunctionReferences());
    }
}
