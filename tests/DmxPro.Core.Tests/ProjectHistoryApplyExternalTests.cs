using DmxPro.Core.Functions;
using DmxPro.Core.History;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>Applying a project file changed by another program in place (<see cref="ProjectHistory.ApplyExternal"/>).</summary>
public class ProjectHistoryApplyExternalTests
{
    private static readonly ProjectSerializer Serializer = new();

    private static (Project Project, ProjectHistory History) Start()
    {
        // As read from a file (migrations applied), so a copy read back again holds the same content.
        var project = Copy(HistoryTestProjects.Rich());
        var history = new ProjectHistory();
        history.Reset(project);
        return (project, history);
    }

    /// <summary>An independent copy, as read back from the file another program wrote.</summary>
    private static Project Copy(Project project) => Serializer.Deserialize(Serializer.Serialize(project)).Project;

    [Fact]
    public void ApplyExternal_AddedScene_AppliedInPlaceKeepsUnchangedObjects()
    {
        var (project, history) = Start();
        var fixtures = project.Fixtures;
        var red = project.Functions[0];
        var external = Copy(project);
        external.CreateFunction(FunctionType.Scene, "Green");

        Assert.True(history.ApplyExternal(project, external, "Reloaded from disk"));

        Assert.Same(fixtures, project.Fixtures);
        Assert.Same(red, project.Functions[0]);
        Assert.Contains(project.Functions, f => f.Name == "Green");
        Assert.Equal(HistoryTestProjects.Json(external), HistoryTestProjects.Json(project));
        Assert.True(history.IsAtSavedState);
        Assert.Equal("Reloaded from disk", history.UndoDescription);
    }

    [Fact]
    public void ApplyExternal_ThenUndo_RestoresPreviousContent()
    {
        var (project, history) = Start();
        var before = HistoryTestProjects.Json(project);
        var external = Copy(project);
        external.Functions.RemoveAt(0);
        history.ApplyExternal(project, external, "Reloaded from disk");

        Assert.Equal("Reloaded from disk", history.Undo(project));

        Assert.Equal(before, HistoryTestProjects.Json(project));
        Assert.False(history.IsAtSavedState);
    }

    [Fact]
    public void ApplyExternal_PendingEdit_BecomesItsOwnStepBeforeTheReload()
    {
        var (project, history) = Start();
        var external = Copy(project);
        project.Fixtures[0].Name = "Mine";
        history.NoteChange(TimeSpan.Zero);

        history.ApplyExternal(project, external, "Reloaded from disk");
        history.Undo(project);

        Assert.Equal("Mine", project.Fixtures[0].Name);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void ApplyExternal_SameContent_AddsNoStepAndMarksSaved()
    {
        var (project, history) = Start();
        project.Fixtures[0].Name = "Mine";
        history.NoteChange(TimeSpan.Zero);
        history.Capture(project);
        Assert.False(history.IsAtSavedState);

        Assert.False(history.ApplyExternal(project, Copy(project), "Reloaded from disk"));

        Assert.True(history.IsAtSavedState);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void ComputeContentHash_SameBytes_SameHashAndDifferentBytesDiffer()
    {
        var a = ProjectSerializer.ComputeContentHash("{\"a\":1}"u8);
        var b = ProjectSerializer.ComputeContentHash("{\"a\":1}"u8);
        var c = ProjectSerializer.ComputeContentHash("{\"a\":2}"u8);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public async Task SaveAsync_ReturnsHashOfWrittenFileAndLoadAsyncReportsIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"DmxPro.Hash-{Guid.NewGuid():N}.dmxpro");
        try
        {
            var hash = await Serializer.SaveAsync(HistoryTestProjects.Rich(), path);

            Assert.Equal(ProjectSerializer.ComputeContentHash(await File.ReadAllBytesAsync(path)), hash);
            Assert.Equal(hash, (await Serializer.LoadAsync(path)).ContentHash);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}
