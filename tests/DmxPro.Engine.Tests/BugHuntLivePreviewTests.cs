using DmxPro.Core.Functions;
using DmxPro.Core.Projects;
using Microsoft.Extensions.Logging;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Bug hunt 2026-10-23: queued-command interleavings around live mode and the live edit preview (drafts), plus
/// damaged-file timing in compiled chasers.
/// </summary>
public class BugHuntLivePreviewTests
{
    private static T DraftOf<T>(T function)
        where T : LightFunction => PreviewDraftTests.DraftOf(function);

    // ---- Bugs (fail until fixed) ------------------------------------------------------------------------------------

    /// <summary>
    /// SetPreviewFunction documents "Draft previews start and stop without fades" and StopPreview honours it, but
    /// StopFunction(id, Preview) on a draft sets Stopping and fades it out with the draft's own fade-out.
    /// </summary>
    [Fact]
    public void StopFunction_DraftPreviewWithFadeOut_StopsAtOnce()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        saved.FadeOutSeconds = 10;
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Engine.StopFunction(saved.Id, EngineLayer.Preview);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.DoesNotContain(rig.Engine.GetSnapshot()!.ActiveFunctions, f => f.Layer == EngineLayer.Preview);
    }

    /// <summary>
    /// A chaser from a damaged/hand-edited file whose step times are finite but sum to +Infinity (two holds of 1e308 s):
    /// the pass period becomes Infinity and <c>cycle * period</c> = 0 × ∞ = NaN, so SlotAt(NaN) picks the LAST step at
    /// t = 0 instead of the first one.
    /// </summary>
    [Fact]
    public void StartFunction_ChaserStepTimesOverflowToInfinity_PlaysFirstStepFirst()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var green = rig.Scene("Green", ("g", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Damaged" });
        chaser.AddStep(red.Id, holdSeconds: 1e308);
        chaser.AddStep(green.Id, holdSeconds: 1e308);
        rig.Load();

        rig.Engine.StartFunction(chaser.Id, EngineLayer.Console);
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Equal(0, rig.At(TestRig.Green));
    }

    /// <summary>
    /// A Release All queued while SetPreviewFunction is still compiling the draft (another thread, or anything that runs
    /// during the compile) is queued BEFORE the draft, so the draft appears after the release. The app's
    /// "check ReleaseGeneration, then SetPreviewFunction" is check-then-act and cannot close this window; the engine
    /// has to drop a draft whose call started before a release that was applied ahead of it. Simulated
    /// deterministically: the compile logs a warning (fade with a deleted scene) and the logger triggers the release.
    /// </summary>
    [Fact]
    public void SetPreviewFunction_ReleaseAllQueuedDuringCompile_DraftNotShownAfterRelease()
    {
        var logger = new CallbackLogger();
        var engine = new DmxEngine(logger) { SnapshotInterval = TimeSpan.Zero };
        var project = new Project();
        var fixture = project.AddFixtures(TestRig.Profile(), "8ch", 1, 1, TestRig.Red, "Par")[0];
        var red = new SceneFunction { Name = "Red" };
        red.SetValue(fixture.Id, "r", 255);
        project.Functions.Add(red);
        engine.LoadProject(project);
        engine.Tick(TimeSpan.Zero);

        // Fade from a deleted scene to red: compiling it logs "uses a scene that does not exist".
        var draft = new FadeFunction { Name = "Fade", FromSceneId = Guid.NewGuid(), ToSceneId = red.Id, DurationSeconds = 0 };
        var generationBefore = engine.ReleaseGeneration;
        logger.OnWarning = () =>
        {
            logger.OnWarning = null;
            engine.ReleaseAll(); // e.g. the operator's Release All from another thread, mid-compile
        };

        engine.SetPreviewFunction(project, draft);
        engine.Tick(TestRig.Frame);

        Assert.NotEqual(generationBefore, engine.ReleaseGeneration); // the release really happened mid-compile
        Assert.Equal(0, engine.GetSnapshot()!.GetValue(1, TestRig.Red));
    }

    // ---- Regression tests for interleavings that work -----------------------------------------------------------------

    [Fact]
    public void Tick_DraftThenLiveOnInSameTick_PreviewNeverRendered()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved)));
        rig.Engine.SetLiveMode(true);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void Tick_LiveOnOffThenDraftInSameTick_DraftShows()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();

        rig.Engine.SetLiveMode(true);
        rig.Engine.SetLiveMode(false);
        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved)));
        rig.Tick();

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_LiveRequestedNotYetApplied_ReturnsFalseAndQueuesNothing()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetLiveMode(true);

        Assert.False(rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved)));
        rig.Engine.SetLiveMode(false);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_DraftThenReleaseAllInSameTick_Removed()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();

        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Engine.ReleaseAll();
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void SetPreviewFunction_ChaserDraftStepReferencesItself_PlaysOtherStepsWithoutHanging()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var chaser = rig.Add(new ChaserFunction { Name = "Loop" });
        rig.Load();
        var draft = DraftOf(chaser);
        draft.AddStep(draft.Id);
        draft.AddStep(red.Id);

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft));
        rig.Tick(0.5);
        Assert.Equal(0, rig.At(TestRig.Red)); // the looping step shows nothing
        rig.Tick(1);

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_ChaserDraftLoopsThroughSavedChaser_PlaysWithoutHanging()
    {
        var rig = new TestRig();
        var red = rig.Scene("Red", ("r", 255));
        var a = rig.Add(new ChaserFunction { Name = "A" });
        var b = rig.Add(new ChaserFunction { Name = "B" });
        b.AddStep(a.Id);
        b.AddStep(red.Id);
        rig.Load();
        var draft = DraftOf(a);
        draft.AddStep(b.Id, holdSeconds: 10); // long enough for B to reach its second step

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft));
        rig.Tick(0.5);
        Assert.Equal(0, rig.At(TestRig.Red)); // B's first step is the loop back to A: nothing
        rig.Tick(1); // 1.5 s: B's second step

        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void SetPreviewFunction_FadeFromDeletedScene_FadesInTargetOnly()
    {
        var rig = new TestRig();
        var green = rig.Scene("Green", ("g", 200));
        rig.Load();
        var draft = new FadeFunction { Name = "Fade", FromSceneId = Guid.NewGuid(), ToSceneId = green.Id, DurationSeconds = 2 };

        Assert.True(rig.Engine.SetPreviewFunction(rig.Project, draft));
        rig.Tick(1);
        Assert.Equal(100, rig.At(TestRig.Green));
        rig.Tick(1.5);

        Assert.Equal(200, rig.At(TestRig.Green));
    }

    [Fact]
    public void RefreshPatch_FixtureDisabledDuringDraft_DropsDraft()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();
        Assert.Equal(255, rig.At(TestRig.Red));

        rig.Fixture.Enabled = false;
        rig.RefreshAndApply();

        Assert.Equal(0, rig.At(TestRig.Red));
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void RefreshPatch_FunctionDeletedWhileDraftPreviewed_KeepsDraft()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved));
        rig.Tick();

        rig.Project.Functions.Remove(saved);
        rig.RefreshAndApply();

        // The draft is not bound to the model; the app clears it when the editor closes.
        Assert.Equal(255, rig.At(TestRig.Red));
    }

    [Fact]
    public void RefreshPatch_TwoRefreshesReaddressAndBackInOneTick_DropsDraftCompiledBetween()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();

        rig.Fixture.StartAddress = 100;
        rig.Engine.RefreshPatch(rig.Project);
        rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved)); // compiled for address 100
        rig.Fixture.StartAddress = TestRig.Red;
        rig.Engine.RefreshPatch(rig.Project);
        rig.Tick();

        Assert.Equal(0, rig.At(100));
        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void StartPreview_DuringLiveMode_RefusedAndNotShownAfterLiveOff()
    {
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetLiveMode(true);
        rig.Engine.StartPreview(saved.Id);
        rig.Tick();

        rig.Engine.SetLiveMode(false);
        rig.Tick();

        Assert.Equal(0, rig.At(TestRig.Red));
    }

    [Fact]
    public void LoadProject_WhileLive_StaysLiveUntilSwitchedOff()
    {
        // Documents engine behaviour: project open does not reset the engine gate; LiveModeService turns it off.
        var rig = new TestRig();
        var saved = rig.Scene("Red", ("r", 255));
        rig.Load();
        rig.Engine.SetLiveMode(true);
        rig.Load();

        Assert.True(rig.Engine.IsLiveMode);
        Assert.False(rig.Engine.SetPreviewFunction(rig.Project, DraftOf(saved)));
    }

    /// <summary>Logger that runs a callback on the first warning (used to inject an action mid-compile).</summary>
    private sealed class CallbackLogger : ILogger<DmxEngine>
    {
        public Action? OnWarning { get; set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                OnWarning?.Invoke();
            }
        }
    }
}
