using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>
/// A chaser editor with own step functions inside the Functions page: Undo / Redo, Cut / Paste and Delete must keep the
/// editor, its draft step functions and the project consistent.
/// </summary>
public sealed class ChaserEditorHostTests : IDisposable
{
    private readonly AppRig _rig = new();
    private readonly string _projectPath = Path.Combine(Path.GetTempPath(), $"DmxPro.App.Tests-{Guid.NewGuid():N}.dmxpro");
    private readonly Fixture _par1;
    private readonly ChaserFunction _chaser;

    public ChaserEditorHostTests()
    {
        _par1 = Project.AddFixtures(Rgbw(), "6ch", 1, 1, 1, "PAR")[0];
        _chaser = new ChaserFunction { Name = "Party" };
        Project.Functions.Add(_chaser);
    }

    private Project Project => _rig.Projects.Current;

    public void Dispose()
    {
        _rig.Dispose();
        try
        {
            File.Delete(_projectPath);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
    }

    // ───────────────────────────── Undo / Redo ─────────────────────────────

    [Fact]
    public async Task Undo_AfterSavedLookEdit_ReopensEditorWithRestoredLook()
    {
        var look = AddOwnLook("Step 1", 10);
        await CommitHistoryAsync();
        var functions = _rig.CreateFunctionsViewModel();
        var editor = await OpenChaserAsync(functions);
        editor.Steps[0].IsSelected = true;
        editor.StepLook!.ApplyPresetCommand.Execute(Preset("Blue"));
        editor.Save();
        Assert.Equal((byte)255, StoredLook(look.Id).GetValue(_par1.Id, "b"));
        await CommitHistoryAsync();

        Assert.True(_rig.Projects.TryUndo(out _));

        // The chaser survived the undo (same object), but its own step look was replaced: the editor reopened.
        Assert.Same(_chaser, Project.FindFunction(_chaser.Id));
        var reopened = Assert.IsType<ChaserEditorViewModel>(functions.Editor);
        Assert.NotSame(editor, reopened);
        Assert.False(reopened.IsStale(Project));

        var restored = StoredLook(look.Id);
        Assert.Equal((byte)10, restored.GetValue(_par1.Id, "r"));
        Assert.Equal((byte)0, restored.GetValue(_par1.Id, "b"));

        // Row (swatch), look panel and preview all show the restored look.
        var row = reopened.Steps[0];
        Assert.Equal((byte)0, ((SceneFunction)row.Function!).GetValue(_par1.Id, "b"));
        Assert.Equal((byte)10, ((SceneFunction)row.Function).GetValue(_par1.Id, "r"));
        row.IsSelected = true;
        Assert.Equal((byte)0, reopened.StepLook!.Scene.GetValue(_par1.Id, "b"));
        var preview = Assert.IsType<SceneFunction>(reopened.PreviewFunctions.Single(f => f.Id == look.Id));
        Assert.Equal((byte)0, preview.GetValue(_par1.Id, "b"));
        Assert.Equal((byte)10, preview.GetValue(_par1.Id, "r"));

        // A following save of another change does not write the undone look back.
        reopened.IsPlayOnce = true;
        reopened.Save();

        Assert.Equal((byte)0, StoredLook(look.Id).GetValue(_par1.Id, "b"));
        Assert.Equal((byte)10, StoredLook(look.Id).GetValue(_par1.Id, "r"));
        Assert.Equal(RunOrder.SingleShot, _chaser.RunOrder);
    }

    [Fact]
    public async Task Undo_WithStepEditorOpen_ReopensChaserWithoutStaleStepEditor()
    {
        var pulse = new PulseFunction { Name = "Throb", FixtureIds = [_par1.Id], OwnerFunctionId = _chaser.Id };
        Project.Functions.Add(pulse);
        _chaser.AddStep(pulse.Id);
        await CommitHistoryAsync();
        var functions = _rig.CreateFunctionsViewModel();
        var editor = await OpenChaserAsync(functions);
        editor.Dialogs = _rig.Dialogs;
        editor.Steps[0].IsSelected = true;
        await editor.EditStepCommand.ExecuteAsync(null);
        var inner = editor.StepEditor!;
        inner.Name = "Throb 2";
        inner.Save();
        await CommitHistoryAsync();

        Assert.True(_rig.Projects.TryUndo(out _));

        var restored = Project.FindFunction(pulse.Id)!;
        Assert.Equal("Throb", restored.Name);
        Assert.NotSame(inner.Original, restored);
        var reopened = Assert.IsType<ChaserEditorViewModel>(functions.Editor);
        Assert.NotSame(editor, reopened);
        Assert.Null(reopened.StepEditor);
        Assert.Equal("Throb", reopened.Steps[0].FunctionName);
    }

    [Fact]
    public async Task Undo_UnrelatedEdit_KeepsChaserEditorAndDraft()
    {
        AddOwnLook("Step 1", 10);
        var other = new SceneFunction { Name = "Other" };
        Project.Functions.Add(other);
        await CommitHistoryAsync();
        var functions = _rig.CreateFunctionsViewModel();
        var editor = await OpenChaserAsync(functions);
        editor.IsPlayOnce = true; // unsaved draft
        other.Name = "Other 2";
        _rig.Projects.MarkChanged();
        await CommitHistoryAsync();

        Assert.True(_rig.Projects.TryUndo(out _));

        Assert.Same(editor, functions.Editor);
        Assert.True(editor.IsDirty);
        Assert.True(editor.IsPlayOnce);
    }

    // ───────────────────────────── Cut / Paste / Delete ─────────────────────────────

    [Fact]
    public async Task CutThenPaste_ChaserWithOwnSteps_StepsComeBack()
    {
        var look = AddOwnLook("Step 1", 42);
        var functions = _rig.CreateFunctionsViewModel();
        functions.SelectedRow = functions.Rows.Single(r => r.Function == _chaser);

        await functions.CutCommand.ExecuteAsync(null);

        Assert.Null(Project.FindFunction(_chaser.Id));
        Assert.Null(Project.FindFunction(look.Id));

        functions.PasteCommand.Execute(null);

        var pasted = Assert.IsType<ChaserFunction>(functions.SelectedRow!.Function);
        var step = Assert.Single(pasted.Steps);
        var scene = Assert.IsType<SceneFunction>(Project.FindFunction(step.FunctionId));
        Assert.Equal(pasted.Id, scene.OwnerFunctionId);
        Assert.Equal((byte)42, scene.GetValue(_par1.Id, "r"));
        Assert.DoesNotContain(scene, Project.FunctionListItems);
    }

    [Fact]
    public async Task Delete_ChaserWithEditorOpen_ClosesEditorAndRemovesOwnSteps()
    {
        var look = AddOwnLook("Step 1", 10);
        var functions = _rig.CreateFunctionsViewModel();
        var editor = await OpenChaserAsync(functions);
        editor.AddStepCommand.Execute(null); // an unsaved own step: never reaches the project
        var draftOnly = editor.Steps[1].Function!.Id;

        await functions.DeleteCommand.ExecuteAsync(null);

        Assert.Null(functions.Editor);
        Assert.Null(Project.FindFunction(_chaser.Id));
        Assert.Null(Project.FindFunction(look.Id));
        Assert.Null(Project.FindFunction(draftOnly));
        Assert.Empty(Project.OwnedFunctions(_chaser.Id));
    }

    // ───────────────────────────── Helpers ─────────────────────────────

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

    /// <summary>
    /// Records the edits so far as one undo step right away (Save captures a pending edit), instead of waiting for the
    /// history's quiet period, so each test step is its own undo step.
    /// </summary>
    private async Task CommitHistoryAsync()
    {
        _rig.Projects.MarkChanged();
        await _rig.Projects.SaveAsync(_projectPath);
    }

    private async Task<ChaserEditorViewModel> OpenChaserAsync(FunctionsViewModel functions)
    {
        functions.SelectedRow = functions.Rows.Single(r => r.Function == _chaser);
        await functions.EditCommand.ExecuteAsync(null);
        return Assert.IsType<ChaserEditorViewModel>(functions.Editor);
    }

    private SceneFunction StoredLook(Guid id) => Assert.IsType<SceneFunction>(Project.FindFunction(id));

    private SceneFunction AddOwnLook(string name, byte red)
    {
        var scene = ChaserSteps.NewOwnLookScene(_chaser.Id, name, []);
        scene.AddFixture(Project, _par1);
        scene.SetValue(_par1.Id, "r", red);
        Project.Functions.Add(scene);
        _chaser.AddStep(scene.Id);
        return scene;
    }
}
