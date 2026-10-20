using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Stage;

namespace DmxPro.Core.Tests;

public class StageSerializationTests
{
    private readonly ProjectSerializer _serializer = new();

    private static Project ProjectWithStage()
    {
        var project = new Project { Name = "Club" };
        project.EmbedProfile(TestProfiles.Rgbw());
        project.Fixtures.Add(new Fixture { Name = "Left", ProfileId = "test-rgbw", ModeName = "8ch", StartAddress = 1 });
        project.Fixtures.Add(new Fixture { Name = "Right", ProfileId = "test-rgbw", ModeName = "8ch", StartAddress = 9 });
        project.Stage.Resize(16, 10);
        project.Stage.Place(project.Fixtures[0].Id, 2.5, 7.25).Rotation = 30;
        project.Stage.Place(project.Fixtures[1].Id, 13.5, 7.25);
        return project;
    }

    [Fact]
    public void Deserialize_ProjectWithStage_RoundTripsLayout()
    {
        var original = ProjectWithStage();

        var loaded = _serializer.Deserialize(_serializer.Serialize(original)).Project;

        Assert.Equal(16, loaded.Stage.Width);
        Assert.Equal(10, loaded.Stage.Depth);
        Assert.Equal(2, loaded.Stage.Placements.Count);
        var left = loaded.Stage.Find(original.Fixtures[0].Id)!;
        Assert.Equal(2.5, left.X);
        Assert.Equal(7.25, left.Y);
        Assert.Equal(30, left.Rotation);
        Assert.Equal(13.5, loaded.Stage.Find(original.Fixtures[1].Id)!.X);
    }

    [Fact]
    public void Serialize_ProjectWithStage_WritesCamelCaseStageObject()
    {
        var json = _serializer.Serialize(ProjectWithStage());

        Assert.Contains("\"stage\": {", json);
        Assert.Contains("\"width\": 16", json);
        Assert.Contains("\"depth\": 10", json);
        Assert.Contains("\"placements\": [", json);
        Assert.Contains("\"fixtureId\":", json);
        Assert.Contains("\"rotation\": 30", json);
        Assert.DoesNotContain("\"count\"", json);
    }

    [Fact]
    public void Deserialize_OldFileWithoutStage_LoadsDefaultStage()
    {
        const string json = """
            { "schemaVersion": 1, "name": "Old show", "fixtures": [] }
            """;

        var project = _serializer.Deserialize(json).Project;

        Assert.NotNull(project.Stage);
        Assert.Equal(StageLayout.DefaultWidth, project.Stage.Width);
        Assert.Equal(StageLayout.DefaultDepth, project.Stage.Depth);
        Assert.Empty(project.Stage.Placements);
    }

    [Fact]
    public void Deserialize_StageNull_LoadsDefaultStage()
    {
        var project = _serializer.Deserialize("""{ "schemaVersion": 1, "stage": null }""").Project;

        Assert.NotNull(project.Stage);
        Assert.NotNull(project.Stage.Placements);
    }

    [Fact]
    public void Deserialize_BadStageValues_LoadsCorrected()
    {
        var fixtureId = Guid.NewGuid();
        var json = $$"""
            {
              "schemaVersion": 1,
              "fixtures": [ { "id": "{{fixtureId}}", "name": "PAR", "universe": 1, "startAddress": 1 } ],
              "stage": {
                "width": -3,
                "depth": 9999,
                "placements": [
                  { "fixtureId": "{{fixtureId}}", "x": 50, "y": -2 },
                  { "fixtureId": "{{fixtureId}}", "x": 1, "y": 1 },
                  { "fixtureId": "{{Guid.NewGuid()}}", "x": 1, "y": 1 }
                ]
              }
            }
            """;

        var project = _serializer.Deserialize(json).Project;

        Assert.Equal(StageLayout.MinSize, project.Stage.Width);
        Assert.Equal(StageLayout.MaxSize, project.Stage.Depth);
        var placement = Assert.Single(project.Stage.Placements);
        Assert.Equal(fixtureId, placement.FixtureId);
        Assert.Equal(StageLayout.MinSize, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void RemoveFixture_Placed_DropsPlacement()
    {
        var project = ProjectWithStage();
        var removed = project.Fixtures[0].Id;

        project.RemoveFixture(removed);

        Assert.Null(project.Stage.Find(removed));
        Assert.Equal(project.Fixtures[0].Id, Assert.Single(project.Stage.Placements).FixtureId);
    }

    [Fact]
    public void DuplicateFixture_PlacedSource_CopyIsNotPlaced()
    {
        var project = ProjectWithStage();

        var copy = project.DuplicateFixture(project.Fixtures[0].Id);

        Assert.Null(project.Stage.Find(copy.Id));
        Assert.Equal(2, project.Stage.Count);
    }

    [Fact]
    public void Stage_SetNull_BecomesDefault()
    {
        var project = new Project { Stage = null! };

        Assert.NotNull(project.Stage);
    }
}
