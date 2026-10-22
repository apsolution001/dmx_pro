using DmxPro.Core.Functions;
using DmxPro.Core.History;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

public class ProjectChangeDescriberTests
{
    private static string Describe(Project project, Action<Project> edit)
    {
        var before = ProjectSnapshot.Capture(project);
        edit(project);
        return ProjectChangeDescriber.Describe(before, ProjectSnapshot.Capture(project, before));
    }

    [Fact]
    public void Describe_OneFixtureAdded_NamesIt()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => p.AddFixtures(TestProfiles.Rgbw(), "8ch", 1, 2, 1, "Wash"));

        Assert.Equal("Add fixture \"Wash\"", text);
    }

    [Fact]
    public void Describe_SeveralFixturesAdded_CountsThem()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => p.AddFixtures(TestProfiles.Rgbw(), "8ch", 3, 2, 1, "Wash"));

        Assert.Equal("Add 3 fixtures", text);
    }

    [Fact]
    public void Describe_FixtureDeletedAlsoFromGroupsAndStage_NamesTheFixture()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => p.RemoveFixture(p.Fixtures[0].Id));

        Assert.Equal("Delete fixture \"Par 1\"", text);
    }

    [Fact]
    public void Describe_SceneValueChanged_EditFunction()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => ((SceneFunction)p.Functions[1]).SetValue(p.Fixtures[0].Id, "b", 1));

        Assert.Equal("Edit function \"Blue\"", text);
    }

    [Fact]
    public void Describe_GroupRenamed_RenameWithOldName()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => p.Groups[0].Name = "Front");

        Assert.Equal("Rename group \"All\"", text);
    }

    [Fact]
    public void Describe_SeveralFunctionsEdited_CountsThem()
    {
        var text = Describe(HistoryTestProjects.Rich(), p =>
        {
            p.Functions[0].Name = "x";
            p.Functions[1].Name = "y";
        });

        Assert.Equal("Edit 2 functions", text);
    }

    [Fact]
    public void Describe_StageMoved_EditStage()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => p.Stage.Place(p.Fixtures[0].Id, 5, 5));

        Assert.Equal("Edit stage", text);
    }

    [Fact]
    public void Describe_FixturesReordered_Reorder()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => p.Fixtures.Reverse());

        Assert.Equal("Reorder fixtures", text);
    }

    [Fact]
    public void Describe_ProjectRenamed_RenameProject()
    {
        var text = Describe(HistoryTestProjects.Rich(), p => p.Name = "Gala");

        Assert.Equal("Rename project", text);
    }

    [Fact]
    public void Describe_LongName_IsShortened()
    {
        var text = Describe(new Project(), p => p.Groups.Add(new FixtureGroup { Name = new string('a', 80) }));

        Assert.True(text.Length < 50, text);
        Assert.EndsWith("…\"", text);
    }

    [Fact]
    public void Describe_NothingChanged_Fallback()
    {
        var text = Describe(HistoryTestProjects.Rich(), _ => { });

        Assert.Equal(ProjectChangeDescriber.Fallback, text);
    }
}
