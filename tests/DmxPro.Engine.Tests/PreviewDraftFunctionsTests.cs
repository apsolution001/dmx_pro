using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Live edit preview of a draft that plays further unsaved functions (a chaser editor's own step scenes that exist only
/// in the editor draft until Save).
/// </summary>
public class PreviewDraftFunctionsTests
{
    [Fact]
    public void SetPreviewFunction_ChaserWithUnsavedStepScene_PlaysTheDraftScene()
    {
        var rig = new TestRig();
        var chaser = new ChaserFunction { Name = "Chase" };
        rig.Project.Functions.Add(chaser);
        rig.Load();

        var draft = PreviewDraftTests.DraftOf(chaser);
        var stepScene = ChaserSteps.NewOwnLookScene(chaser.Id, "Step 1", [new SceneValue { FixtureId = rig.Fixture.Id, ChannelKey = "r", Value = 123 }]);
        draft.AddStep(stepScene.Id, holdSeconds: 10);

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft, [stepScene]));
        rig.Tick();

        Assert.Equal(123, rig.At(TestRig.Red));
        Assert.DoesNotContain(rig.Project.Functions, f => f.Id == stepScene.Id); // nothing added to the project
    }

    [Fact]
    public void SetPreviewFunction_DraftStepSceneReplacesSavedOne()
    {
        var rig = new TestRig();
        var chaser = new ChaserFunction { Name = "Chase" };
        rig.Project.Functions.Add(chaser);
        var saved = rig.Scene("Step 1", ("r", 255));
        saved.OwnerFunctionId = chaser.Id;
        chaser.AddStep(saved.Id, holdSeconds: 10);
        rig.Load();

        var draftScene = PreviewDraftTests.DraftOf(saved);
        draftScene.SetValue(rig.Fixture.Id, "r", 40);

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, PreviewDraftTests.DraftOf(chaser), [draftScene]));
        rig.Tick();

        Assert.Equal(40, rig.At(TestRig.Red));
        Assert.Equal((byte)255, saved.GetValue(rig.Fixture.Id, "r")); // the saved scene is untouched
    }

    [Fact]
    public void SetPreviewFunction_WithoutDraftFunctions_UsesSavedSteps()
    {
        var rig = new TestRig();
        var chaser = new ChaserFunction { Name = "Chase" };
        rig.Project.Functions.Add(chaser);
        var saved = rig.Scene("Step 1", ("r", 200));
        chaser.AddStep(saved.Id, holdSeconds: 10);
        rig.Load();

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, PreviewDraftTests.DraftOf(chaser)));
        rig.Tick();

        Assert.Equal(200, rig.At(TestRig.Red));
    }
}
