using DmxPro.Core.Functions;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;

namespace DmxPro.Core.Tests;

/// <summary>Projects with every kind of content, for the undo/redo tests.</summary>
internal static class HistoryTestProjects
{
    private static readonly ProjectSerializer Serializer = new();

    /// <summary>Profiles, fixtures, a group, scenes, a chaser, a show with an own copy, a console page and a stage.</summary>
    public static Project Rich()
    {
        var project = new Project { Name = "Wedding" };
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 4, 1, 1, "Par");
        project.Groups.Add(new FixtureGroup { Name = "All", FixtureIds = fixtures.Select(f => f.Id).ToList() });

        var red = (SceneFunction)project.CreateFunction(FunctionType.Scene, "Red");
        var blue = (SceneFunction)project.CreateFunction(FunctionType.Scene, "Blue");
        foreach (var fixture in fixtures)
        {
            red.SetValue(fixture.Id, "r", 255);
            blue.SetValue(fixture.Id, "b", 200);
        }

        var chaser = (ChaserFunction)project.CreateFunction(FunctionType.Chaser, "Red Blue");
        chaser.Steps.Add(new ChaserStep { FunctionId = red.Id });
        chaser.Steps.Add(new ChaserStep { FunctionId = blue.Id });

        var show = project.CreateShow("First dance");
        project.CreateFunctionInShow(show.Id, show.Tracks[0].Id, FunctionType.Pulse, "Pulse", 0, 4);
        project.CreateDefaultConsolePage("Main");
        project.Stage.Place(fixtures[0].Id, 2, 3);
        return project;
    }

    /// <summary>The project as its saved file would look.</summary>
    public static string Json(Project project) => Serializer.Serialize(project);
}
