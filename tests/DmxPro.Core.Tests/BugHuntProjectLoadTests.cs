using System.Text;
using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Serialization;
using DmxPro.Core.Testing;

namespace DmxPro.Core.Tests;

/// <summary>Bug hunt: project loading (schemaVersion reader, stage repair) and patch health.</summary>
public class BugHuntProjectLoadTests
{
    private readonly ProjectSerializer _serializer = new();

    [Fact]
    public void Deserialize_SchemaVersionLastAndNestedSchemaVersionKeys_UsesTopLevelValue()
    {
        const string json = """
            {
              // comment before
              "name": "X",
              "fixtures": [ { "schemaVersion": 99 } ],
              "stage": { "schemaVersion": "abc", "width": 5 },
              "schemaVersion": 1,
            }
            """;

        var result = _serializer.Deserialize(json);

        Assert.Equal("X", result.Project.Name);
        Assert.Equal(5, result.Project.Stage.Width);
    }

    [Fact]
    public void Deserialize_Utf8BomBytes_Loads()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{"schemaVersion":1,"name":"B"}""")).ToArray();

        Assert.Equal("B", _serializer.Deserialize(bytes).Project.Name);
    }

    [Theory]
    [InlineData("""{"schemaVersion":"1"}""")]
    [InlineData("""{"schemaVersion":1.5}""")]
    [InlineData("""{"schemaVersion":1} trailing""")]
    [InlineData("""{"schemaVersion":1""")]
    [InlineData("""[{"schemaVersion":1}]""")]
    [InlineData("")]
    [InlineData("42")]
    public void Deserialize_BadSchemaVersionOrDocument_ThrowsFileFormatException(string json)
    {
        Assert.Throws<DmxFileFormatException>(() => _serializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_StageWithNullsDuplicatesAndOrphans_IsRepaired()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "schemaVersion": 1,
              "profiles": [],
              "fixtures": [ { "id": "{{id}}", "profileId": "x", "modeName": "m" } ],
              "stage": {
                "width": 1e300,
                "depth": -4,
                "placements": [
                  null,
                  { "fixtureId": "{{id}}", "x": 1e308, "y": -1, "rotation": 10 },
                  { "fixtureId": "{{id}}", "x": 2, "y": 2 },
                  { "fixtureId": "{{Guid.NewGuid()}}", "x": 1, "y": 1 }
                ]
              }
            }
            """;

        var stage = _serializer.Deserialize(json).Project.Stage;

        Assert.Equal(200, stage.Width);
        Assert.Equal(1, stage.Depth);
        var placement = Assert.Single(stage.Placements);
        Assert.Equal(200, placement.X);
        Assert.Equal(0, placement.Y);
        Assert.Same(placement, stage.Find(id));
    }

    [Fact]
    public void Deserialize_StageNull_GetsDefaultStage()
    {
        var project = _serializer.Deserialize("""{"schemaVersion":1,"stage":null}""").Project;

        Assert.Equal(0, project.Stage.Count);
    }

    [Fact]
    public void Deserialize_SceneWithNullValues_LoadsOrReportsDamagedFile()
    {
        const string json = """
            {"schemaVersion":1,"functions":[{"type":"scene","id":"6f1c3a52-6b0e-4a3e-9d07-3d9d1d0f0a11","name":"S","values":null}]}
            """;

        var ex = Record.Exception(() => _serializer.Deserialize(json));

        Assert.True(ex is null or DmxFileFormatException, $"Unexpected {ex?.GetType().Name}: {ex?.Message}");
    }

    [Fact]
    public void Analyze_FixturesInInvalidUniverse_NotReportedAsOverlapping()
    {
        // PatchHealth claims to use "the same rule as the patch validator", which skips fixtures in invalid universes.
        var project = new Project();
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2, 1, 1, "Par");
        fixtures[0].Universe = 0;
        fixtures[1].Universe = 0;
        fixtures[1].StartAddress = 1;

        var health = PatchHealth.Analyze(project);
        var validatorOverlaps = PatchValidator.Validate(project.Fixtures, project).Count(i => i.Code == "ADDRESS_OVERLAP");

        Assert.Equal(0, validatorOverlaps);
        Assert.Equal(0, health.OverlapCount);
    }

    [Fact]
    public void Analyze_FootprintsPast512_NotReportedAsOverlapping()
    {
        var project = new Project();
        var fixtures = project.AddFixtures(TestProfiles.Rgbw(), "8ch", 2, 1, 1, "Par");
        fixtures[0].StartAddress = 509; // 509–516: past 512
        fixtures[1].StartAddress = 505; // 505–512: valid, shares 509–512 with an unsendable fixture

        var health = PatchHealth.Analyze(project);
        var validatorOverlaps = PatchValidator.Validate(project.Fixtures, project).Count(i => i.Code == "ADDRESS_OVERLAP");

        Assert.Equal(validatorOverlaps, health.OverlapCount);
    }
}
