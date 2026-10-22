using DmxPro.Core.Functions;

namespace DmxPro.Engine.Tests;

/// <summary>Live edit preview: an editor's unsaved draft shown in the preview layer (owner decision 2026-10-22).</summary>
public class PreviewDraftTests
{
    /// <summary>An unsaved copy of <paramref name="function"/> with the same id (what an open editor produces).</summary>
    internal static T DraftOf<T>(T function)
        where T : LightFunction
    {
        var draft = (T)function.CloneAsNew();
        draft.Id = function.Id;
        return draft;
    }

    private static ActiveFunctionInfo PreviewInfo(TestRig rig) =>
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions, f => f.Layer == EngineLayer.Preview);

    [Fact]
    public void SetPreviewFunction_UnsavedSceneDraft_SetsExactChannelValues()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        var draft = DraftOf(saved);
        draft.SetValue(rig.Fixture.Id, "r", 100);
        draft.SetValue(rig.Fixture.Id, "g", 50);
        draft.SetValue(rig.Fixture.Id, "macro", 30);

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft));
        rig.Tick();

        var expected = TestRig.DefaultsFrame();
        expected[DmxPro.Core.Patching.DmxAddress.ToIndex(TestRig.Red)] = 100;
        expected[DmxPro.Core.Patching.DmxAddress.ToIndex(TestRig.Green)] = 50;
        expected[DmxPro.Core.Patching.DmxAddress.ToIndex(TestRig.Macro)] = 30;
        Assert.Equal(expected, rig.Universe());
        Assert.Equal(255, saved.Values.Single().Value); // the saved scene is untouched
    }

    [Fact]
    public void SetPreviewFunction_FunctionNotInProject_Plays()
    {
        var rig = new TestRig();
        rig.Load();
        var draft = new SceneFunction { Name = "New" };
        draft.SetValue(rig.Fixture.Id, "b", 77);

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft));
        rig.Tick();

        Assert.Equal(77, rig.At(TestRig.Blue));
        Assert.Equal(draft.Id, PreviewInfo(rig).FunctionId);
    }

    [Fact]
    public void SetPreviewFunction_SceneWithFadeIn_ShowsAtOnce()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Slow", ("r", 200));
        saved.FadeInSeconds = 5;
        rig.Load();

        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();

        Assert.Equal(200, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_EditedAgain_UpdatesValuesInPlace()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        var draft = DraftOf(saved);
        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick();

        draft.SetValue(rig.Fixture.Id, "r", 10);
        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick();

        Assert.Equal(10, rig.At(TestRig.Red));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void SetPreviewFunction_ChaserEdited_KeepsRunningWithoutRestart()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Chase" });
        chaser.AddStep(red.Id);
        chaser.AddStep(green.Id);
        rig.Load();
        var draft = DraftOf(chaser);
        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick(1.5); // second step (1 s hold each)
        Assert.Equal(255, rig.At(TestRig.Green));

        draft.Steps[1].HoldSeconds = 3;
        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick(1); // 2.5 s: still in the (now longer) second step, not restarted at step 1

        Assert.Equal(255, rig.At(TestRig.Green));
        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_ChaserDraft_UsesSavedScenesOfSteps()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        rig.Load();
        var draft = new ChaserFunction { Name = "New chase" };
        draft.AddStep(red.Id);

        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick(0.5);

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void ClearPreview_Running_RestoresWhatRunsUnderneath()
    {
        var rig = new TestRig();
        var blue = rig.Scene("Blue", ("b", 255), ("r", 20));
        var saved = rig.Scene("Red", ("r", 255));
        saved.FadeOutSeconds = 3;
        rig.Load();
        rig.Engine.StartFunction(blue.Id, EngineLayer.Console);
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Engine.ClearPreview();
        rig.Tick();

        Assert.Equal(20, rig.At(TestRig.Red));
        Assert.Equal(255, rig.At(TestRig.Blue));
        Assert.DoesNotContain(rig.Engine.GetSnapshot()!.ActiveFunctions, f => f.Layer == EngineLayer.Preview);
    }

    [Fact]
    public void ClearPreview_Running_ReturnsToDefaults()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255), ("dim", 10));
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();

        rig.Engine.ClearPreview();
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
    }

    [Fact]
    public void StopPreview_DraftWithFadeOut_StopsAtOnce()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        saved.FadeOutSeconds = 3;
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();

        rig.Engine.StopPreview();
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_OtherPreviewRunning_ReplacesIt()
    {
        var rig = new TestRig();
        var green = rig.Scene("Green", ("g", 255));
        var red = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.StartPreview(green.Id);
        rig.Tick();

        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(red));
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Green));
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(red.Id, PreviewInfo(rig).FunctionId);
    }

    [Fact]
    public void StartPreview_SameIdAsDraft_PlaysSavedFunction()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        var draft = DraftOf(saved);
        draft.SetValue(rig.Fixture.Id, "r", 10);
        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick();

        rig.Engine.StartPreview(saved.Id);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void ReleaseAll_DraftPreview_Removed()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();

        rig.Engine.ReleaseAll();
        rig.Tick();

        Assert.Equal(TestRig.DefaultsFrame(), rig.Universe());
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void LoadProject_DraftPreview_Removed()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();

        rig.Load();

        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void RefreshPatch_DraftOfSavedFunction_KeepsDraftValues()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        var draft = DraftOf(saved);
        draft.SetValue(rig.Fixture.Id, "r", 40);
        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick();

        rig.Scene("Other", ("g", 1)); // an unrelated edit recompiles the model
        rig.RefreshAndApply();
        rig.Tick();

        Assert.Equal(40, rig.At(TestRig.Red)); // still the draft, not re-bound to the saved scene by id
    }

    [Fact]
    public void RefreshPatch_UniverseLayoutChanged_DropsDraft()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();

        rig.Project.AddFixtures(TestRig.Profile(), "8ch", 1, 2, 1, "Other universe");
        rig.RefreshAndApply();
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void RefreshPatch_FixtureReaddressedSameLayout_DropsStaleDraftUntilResent()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        var draft = DraftOf(saved);
        draft.SetValue(rig.Fixture.Id, "r", 200);
        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Red));

        rig.Fixture.StartAddress = 100; // same universe layout, different flat channels
        rig.RefreshAndApply();
        rig.Tick();

        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
        Assert.Equal(0, rig.At(TestRig.Red)); // the old address is no longer the fixture's red
        Assert.Equal(0, rig.At(100)); // and the stale draft did not land anywhere

        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick();

        Assert.Equal(200, rig.At(100));
        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_QueuedBeforeReaddressing_NeverPlaysAfterRefresh()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        var draft = DraftOf(saved);
        draft.SetValue(rig.Fixture.Id, "r", 200);
        rig.Engine.SetPreviewFunction(rig.Project, draft); // compiled against the old patch

        rig.Fixture.StartAddress = 100;
        rig.Engine.RefreshPatch(rig.Project);
        rig.Tick();

        var frame = rig.Universe();
        var expected = new byte[512];
        expected[DmxPro.Core.Patching.DmxAddress.ToIndex(100 + 4)] = 255; // only the re-addressed dimmer default
        Assert.Equal(expected, frame);
    }

    [Theory]
    [InlineData(FunctionType.Flash, true)]
    [InlineData(FunctionType.Scene, false)]
    [InlineData(FunctionType.Chaser, false)]
    [InlineData(FunctionType.Sequence, false)]
    [InlineData(FunctionType.ColorCycle, false)]
    [InlineData(FunctionType.Fade, false)]
    [InlineData(FunctionType.Pulse, false)]
    [InlineData(FunctionType.Strobe, false)]
    public void ReleasesOnFocusLoss_FunctionType_OnlyFlashReleases(FunctionType type, bool expected)
    {
        Assert.Equal(expected, PreviewFocusPolicy.ReleasesOnFocusLoss(type));
    }

    [Fact]
    public void SetPreviewFunction_TimedFlash_PreviewsAsHeld()
    {
        var rig = new TestRig();
        var flash = rig.Add(new FlashFunction { Name = "Flash", FixtureIds = [rig.Fixture.Id], Color = new RgbColor(255, 0, 0), DurationSeconds = 0.5 });
        rig.Load();

        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(flash));
        rig.Tick(2);

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_StrobeNotAllowed_SteadyLight()
    {
        var rig = new TestRig();
        var strobe = rig.Add(new StrobeFunction { Name = "Strobe", FixtureIds = [rig.Fixture.Id], RateHz = 5 });
        rig.Load();

        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(strobe));
        for (var i = 0; i < 20; i++)
        {
            rig.Tick();
            Assert.Equal(255, rig.At(TestRig.Dimmer));
        }

        Assert.Equal(StrobeLimit.Suppressed, PreviewInfo(rig).StrobeLimit);
    }

    [Fact]
    public void SetPreviewFunction_StrobeAboveMaximumRate_IsCapped()
    {
        var rig = new TestRig();
        var strobe = rig.Add(new StrobeFunction { Name = "Strobe", FixtureIds = [rig.Fixture.Id], RateHz = 5 });
        rig.Load();
        rig.Engine.AllowStrobe = true;
        rig.Engine.MaxStrobeRateHz = 10;
        var draft = DraftOf(strobe);
        draft.RateHz = 20; // edited in the draft above the limit

        rig.Engine.SetPreviewFunction(rig.Project, draft);
        rig.Tick(0.07); // at 10 Hz: phase 0.7 → off (at 20 Hz it would be phase 0.4 → on)

        Assert.Equal(0, rig.At(TestRig.Dimmer));
        Assert.Equal(StrobeLimit.RateLimited, PreviewInfo(rig).StrobeLimit);
    }

    [Fact]
    public void Tick_DraftPreviewRunning_AllocatesNothing()
    {
        var rig = new TestRig();
        var cycle = rig.Add(new ColorCycleFunction { Name = "Cycle", FixtureIds = [rig.Fixture.Id] });
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(cycle));
        for (var i = 0; i < 100; i++)
        {
            rig.Tick();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            rig.Tick();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
