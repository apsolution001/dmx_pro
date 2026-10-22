using System.Reflection;
using System.Text.Json.Serialization;
using DmxPro.Core.Functions;
using DmxPro.Core.History;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class ProjectSnapshotTests
{
    [Fact]
    public void ToProject_RichProject_SerializesExactlyLikeTheOriginal()
    {
        var project = HistoryTestProjects.Rich();

        var copy = ProjectSnapshot.Capture(project).ToProject();

        Assert.Equal(HistoryTestProjects.Json(project), HistoryTestProjects.Json(copy));
    }

    [Fact]
    public void Capture_NothingChanged_SharesEverySection()
    {
        var project = HistoryTestProjects.Rich();
        var first = ProjectSnapshot.Capture(project);

        var second = ProjectSnapshot.Capture(project, first);

        Assert.True(second.ContentEquals(first));
        Assert.Same(first.Fixtures, second.Fixtures);
        Assert.Same(first.Functions, second.Functions);
        Assert.Same(first.Stage, second.Stage);
    }

    [Fact]
    public void Capture_OneFunctionEdited_SharesTheOtherItems()
    {
        var project = HistoryTestProjects.Rich();
        var first = ProjectSnapshot.Capture(project);
        project.Functions[1].Name = "Deep blue";

        var second = ProjectSnapshot.Capture(project, first);

        Assert.False(second.ContentEquals(first));
        Assert.Same(first.Fixtures, second.Fixtures);
        Assert.Same(first.Functions.Items[0], second.Functions.Items[0]);
        Assert.NotSame(first.Functions.Items[1], second.Functions.Items[1]);
    }

    [Fact]
    public void RestoreInto_EditUndone_KeepsProjectListsAndUnchangedItemsAsSameObjects()
    {
        var project = HistoryTestProjects.Rich();
        var before = ProjectSnapshot.Capture(project);
        var json = HistoryTestProjects.Json(project);
        var functions = project.Functions;
        var red = project.Functions[0];
        var blue = (SceneFunction)project.Functions[1];
        var fixture = project.Fixtures[0];
        blue.SetValue(fixture.Id, "b", 10);
        var after = ProjectSnapshot.Capture(project, before);

        before.RestoreInto(project, after);

        Assert.Equal(json, HistoryTestProjects.Json(project));
        Assert.Same(functions, project.Functions);
        Assert.Same(red, project.Functions[0]);
        Assert.Same(fixture, project.Fixtures[0]);
        Assert.NotSame(blue, project.Functions[1]);
        Assert.Equal(blue.Id, project.Functions[1].Id);
    }

    [Fact]
    public void RestoreInto_DeletedFixtureBroughtBack_RestoresGroupsAndStage()
    {
        var project = HistoryTestProjects.Rich();
        var before = ProjectSnapshot.Capture(project);
        var json = HistoryTestProjects.Json(project);
        project.RemoveFixture(project.Fixtures[0].Id);
        var after = ProjectSnapshot.Capture(project, before);

        before.RestoreInto(project, after);

        Assert.Equal(json, HistoryTestProjects.Json(project));
        Assert.Equal(4, project.Fixtures.Count);
        Assert.NotNull(project.FindFixture(project.Fixtures[0].Id));
        Assert.Equal(1, project.Stage.Count);
    }

    [Fact]
    public void RestoreInto_Reordered_RestoresOrder()
    {
        var project = HistoryTestProjects.Rich();
        var before = ProjectSnapshot.Capture(project);
        var first = project.Fixtures[0];
        project.Fixtures.RemoveAt(0);
        project.Fixtures.Add(first);
        var after = ProjectSnapshot.Capture(project, before);

        before.RestoreInto(project, after);

        Assert.Same(first, project.Fixtures[0]);
    }

    [Fact]
    public void RestoreInto_ProjectRenamed_RestoresName()
    {
        var project = HistoryTestProjects.Rich();
        var before = ProjectSnapshot.Capture(project);
        project.Name = "Gala";
        var after = ProjectSnapshot.Capture(project, before);

        before.RestoreInto(project, after);

        Assert.Equal("Wedding", project.Name);
    }

    [Fact]
    public void RestoreInto_AutomaticPlacementOverEmptyStage_KeepsPlacement()
    {
        var project = new Project();
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 2, 1, 1, "Par");
        var empty = ProjectSnapshot.Capture(project);
        project.Stage.Place(fixtures[0].Id, 1, 1);
        project.Stage.IsArranged = false; // what the Stage page does for its automatic first placement
        var placed = ProjectSnapshot.Capture(project, empty);

        empty.RestoreInto(project, placed);

        Assert.Equal(1, project.Stage.Count);
    }

    [Fact]
    public void RestoreInto_ArrangedStageOverEmptyStage_RestoresEmptyStage()
    {
        var project = new Project();
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 2, 1, 1, "Par");
        var empty = ProjectSnapshot.Capture(project);
        project.Stage.Place(fixtures[0].Id, 1, 1);
        var placed = ProjectSnapshot.Capture(project, empty);

        empty.RestoreInto(project, placed);

        Assert.Equal(0, project.Stage.Count);
    }

    [Fact]
    public void RestoreInto_CurrentNotFromThisProject_Throws()
    {
        var project = HistoryTestProjects.Rich();
        var other = ProjectSnapshot.Capture(new Project());

        Assert.Throws<InvalidOperationException>(() => ProjectSnapshot.Capture(project).RestoreInto(project, other));
    }

    /// <summary>
    /// A new saved project property must also be undone: add it to <see cref="ProjectSnapshot"/> (Capture, RestoreInto,
    /// ContentEquals) and then to this list.
    /// </summary>
    [Fact]
    public void Project_SavedProperties_AreAllCoveredBySnapshots()
    {
        var saved = typeof(Project)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => p.Name)
            .Order()
            .ToList();

        Assert.Equal(
            ["ConsolePages", "Fixtures", "Functions", "Groups", "Id", "Name", "Profiles", "SchemaVersion", "Shows", "Stage"],
            saved);
    }
}
