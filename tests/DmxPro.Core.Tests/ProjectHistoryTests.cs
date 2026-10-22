using DmxPro.Core.Functions;
using DmxPro.Core.History;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class ProjectHistoryTests
{
    private static readonly TimeSpan Quiet = ProjectHistory.DefaultQuietPeriod;

    private static (Project Project, ProjectHistory History) Start()
    {
        var project = HistoryTestProjects.Rich();
        var history = new ProjectHistory();
        history.Reset(project);
        return (project, history);
    }

    /// <summary>An edit reported like ProjectService.MarkChanged does, then captured once the edits pause.</summary>
    private static void Edit(ProjectHistory history, Project project, Action edit, ref TimeSpan now)
    {
        edit();
        history.NoteChange(now);
        now += Quiet;
        Assert.Equal(TimeSpan.Zero, history.TimeUntilCaptureDue(now));
        history.Capture(project);
    }

    [Fact]
    public void Reset_NewProject_NothingToUndoAndSaved()
    {
        var (_, history) = Start();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.True(history.IsAtSavedState);
    }

    [Fact]
    public void Undo_AfterEdit_RestoresContentAndDescribesStep()
    {
        var (project, history) = Start();
        var json = HistoryTestProjects.Json(project);
        var now = TimeSpan.Zero;
        Edit(history, project, () => project.RenameFunction(project.Functions[0].Id, "Crimson"), ref now);

        Assert.Equal("Rename function \"Red\"", history.UndoDescription);
        Assert.Equal("Rename function \"Red\"", history.Undo(project));

        Assert.Equal(json, HistoryTestProjects.Json(project));
        Assert.True(history.IsAtSavedState);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void Redo_AfterUndo_ReappliesEdit()
    {
        var (project, history) = Start();
        var now = TimeSpan.Zero;
        Edit(history, project, () => project.Fixtures[0].StartAddress = 200, ref now);
        var edited = HistoryTestProjects.Json(project);
        history.Undo(project);

        Assert.Equal("Edit fixture \"Par 1\"", history.Redo(project));

        Assert.Equal(edited, HistoryTestProjects.Json(project));
        Assert.False(history.IsAtSavedState);
    }

    [Fact]
    public void Undo_PendingEditNotCapturedYet_CapturesAndUndoesIt()
    {
        var (project, history) = Start();
        var json = HistoryTestProjects.Json(project);
        project.Fixtures[1].Name = "Left";
        history.NoteChange(TimeSpan.Zero);

        Assert.True(history.CanUndo);
        Assert.False(history.IsAtSavedState);
        Assert.NotNull(history.Undo(project));

        Assert.Equal(json, HistoryTestProjects.Json(project));
    }

    [Fact]
    public void NoteChange_BurstWithinQuietPeriod_IsOneStep()
    {
        var (project, history) = Start();
        var json = HistoryTestProjects.Json(project);
        var scene = (SceneFunction)project.Functions[0];
        var now = TimeSpan.Zero;

        // A fader drag: many edits, each well inside the quiet period of the previous one.
        for (var value = 0; value < 50; value++)
        {
            scene.SetValue(project.Fixtures[0].Id, "r", (byte)value);
            history.NoteChange(now);
            now += TimeSpan.FromMilliseconds(30);
            Assert.True(history.TimeUntilCaptureDue(now) > TimeSpan.Zero);
        }

        now += Quiet;
        history.Capture(project);

        Assert.Equal(1, history.UndoCount);
        history.Undo(project);
        Assert.Equal(json, HistoryTestProjects.Json(project));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Capture_EditsSeparatedByPause_AreSeparateSteps()
    {
        var (project, history) = Start();
        var now = TimeSpan.Zero;
        Edit(history, project, () => project.Fixtures[0].Name = "A", ref now);
        Edit(history, project, () => project.Fixtures[1].Name = "B", ref now);

        Assert.Equal(2, history.UndoCount);
        history.Undo(project);
        Assert.Equal("A", project.Fixtures[0].Name);
        Assert.Equal("Par 2", project.Fixtures[1].Name);
    }

    [Fact]
    public void Capture_EditThatChangedNothing_AddsNoStep()
    {
        var (project, history) = Start();
        history.NoteChange(TimeSpan.Zero);

        history.Capture(project);

        Assert.False(history.CanUndo);
        Assert.True(history.IsAtSavedState);
    }

    [Fact]
    public void Capture_UnreportedChange_FoldedIntoCurrentStateWithoutStep()
    {
        var (project, history) = Start();
        project.Shows[0].Name = "Relinked"; // e.g. a save relinking music paths, never reported as an edit

        history.Capture(project);

        Assert.False(history.CanUndo);
        Assert.True(history.IsAtSavedState);
    }

    [Fact]
    public void NewEdit_AfterUndo_ClearsRedo()
    {
        var (project, history) = Start();
        var now = TimeSpan.Zero;
        Edit(history, project, () => project.Fixtures[0].Name = "A", ref now);
        history.Undo(project);

        project.Fixtures[1].Name = "B";
        history.NoteChange(now);

        Assert.False(history.CanRedo);
        history.Capture(project);
        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void MarkSaved_ThenUndoAndRedo_SavedStateFollows()
    {
        var (project, history) = Start();
        var now = TimeSpan.Zero;
        Edit(history, project, () => project.Fixtures[0].Name = "A", ref now);
        project.Fixtures[1].Name = "B";
        history.NoteChange(now);

        var token = history.PrepareSave(project);
        Assert.True(history.MarkSaved(token));

        Assert.True(history.IsAtSavedState);
        history.Undo(project);
        Assert.False(history.IsAtSavedState);
        history.Redo(project);
        Assert.True(history.IsAtSavedState);
    }

    [Fact]
    public void MarkSaved_EditCapturedWhileSaving_NotSaved()
    {
        var (project, history) = Start();
        var token = history.PrepareSave(project);
        project.Fixtures[0].Name = "A";
        history.NoteChange(TimeSpan.Zero);
        history.Capture(project);

        Assert.False(history.MarkSaved(token));
        Assert.False(history.IsAtSavedState);
    }

    [Fact]
    public void Undo_ManySteps_StopsAtCap()
    {
        var project = HistoryTestProjects.Rich();
        var history = new ProjectHistory(maxSteps: 5);
        history.Reset(project);
        var now = TimeSpan.Zero;
        for (var i = 0; i < 8; i++)
        {
            var name = $"Name {i}";
            Edit(history, project, () => project.Fixtures[0].Name = name, ref now);
        }

        var undone = 0;
        while (history.Undo(project) is not null)
        {
            undone++;
        }

        Assert.Equal(5, undone);
        Assert.Equal("Name 2", project.Fixtures[0].Name);
        Assert.False(history.IsAtSavedState);
    }

    [Fact]
    public void Undo_DeleteFunction_BringsItBackWithSameId()
    {
        var (project, history) = Start();
        var json = HistoryTestProjects.Json(project);
        var id = project.Functions[0].Id;
        var now = TimeSpan.Zero;
        Edit(history, project, () => project.DeleteFunction(id), ref now);

        Assert.Equal("Delete function \"Red\"", history.UndoDescription);
        history.Undo(project);

        Assert.NotNull(project.FindFunction(id));
        Assert.Equal(json, HistoryTestProjects.Json(project));
    }
}
