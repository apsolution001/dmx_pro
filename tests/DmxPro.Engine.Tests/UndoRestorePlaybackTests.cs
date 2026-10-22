using DmxPro.Core.Functions;
using DmxPro.Core.History;

namespace DmxPro.Engine.Tests;

/// <summary>
/// Undo / Redo restore project content in place and the app then calls <see cref="DmxEngine.RefreshPatch"/> (never
/// <see cref="DmxEngine.LoadProject"/>): what runs keeps running, with the restored content.
/// </summary>
public class UndoRestorePlaybackTests
{
    private static ProjectHistory Started(TestRig rig)
    {
        var history = new ProjectHistory();
        history.Reset(rig.Project);
        return history;
    }

    private static void EditAndCapture(ProjectHistory history, TestRig rig, Action edit)
    {
        edit();
        history.NoteChange(TimeSpan.Zero);
        history.Capture(rig.Project);
    }

    [Fact]
    public void Undo_SceneValueEditWhileRunning_KeepsRunningWithRestoredValue()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        var history = Started(rig);
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();
        EditAndCapture(history, rig, () => scene.SetValue(rig.Fixture.Id, "r", 100));
        rig.RefreshAndApply();
        Assert.Equal(100, rig.At(TestRig.Red));

        history.Undo(rig.Project);
        rig.RefreshAndApply();

        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void Undo_AddedFunctionThatIsRunning_StopsIt()
    {
        var rig = new TestRig();
        var history = Started(rig);
        rig.Load();
        SceneFunction? added = null;
        EditAndCapture(history, rig, () => added = rig.Scene("Blue", ("b", 200)));
        rig.RefreshAndApply();
        rig.Engine.StartFunction(added!.Id, EngineLayer.Console);
        rig.Tick();
        Assert.Equal(200, rig.At(TestRig.Blue));

        history.Undo(rig.Project);
        rig.RefreshAndApply();

        Assert.Equal(0, rig.At(TestRig.Blue));
        Assert.Empty(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }

    [Fact]
    public void Undo_UnrelatedEdit_RunningFunctionUntouched()
    {
        var rig = new TestRig();
        var scene = rig.Scene("Red", ("r", 255));
        var history = Started(rig);
        rig.Load();
        rig.Engine.StartFunction(scene.Id, EngineLayer.Console);
        rig.Tick();
        EditAndCapture(history, rig, () => rig.Fixture.Name = "Front");

        history.Undo(rig.Project);
        rig.RefreshAndApply();

        Assert.Same(scene, rig.Project.Functions[0]);
        Assert.Equal(255, rig.At(TestRig.Red));
        Assert.Single(rig.Engine.GetSnapshot()!.ActiveFunctions);
    }
}
