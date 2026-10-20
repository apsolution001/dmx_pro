using DmxPro.Core.Patching;
using DmxPro.Core.Projects;
using DmxPro.Core.Stage;

namespace DmxPro.Core.Tests;

/// <summary><see cref="StageLayout.IsArranged"/> and the fixture-id index of <see cref="StageLayout.Find"/>.</summary>
public class StageLayoutArrangedTests
{
    private readonly ProjectSerializer _serializer = new();

    private static List<Fixture> Fixtures(int count) =>
        Enumerable.Range(0, count).Select(i => new Fixture { Name = $"F{i}", StartAddress = 1 + i }).ToList();

    [Fact]
    public void IsArranged_NewLayout_IsFalse()
    {
        Assert.False(new StageLayout().IsArranged);
    }

    [Fact]
    public void Place_NewLayout_SetsIsArranged()
    {
        var layout = new StageLayout();

        layout.Place(Guid.NewGuid(), 1, 1);

        Assert.True(layout.IsArranged);
    }

    [Fact]
    public void Remove_FixtureNotPlaced_LeavesIsArrangedFalse()
    {
        var layout = new StageLayout();

        Assert.False(layout.Remove(Guid.NewGuid()));

        Assert.False(layout.IsArranged);
    }

    [Fact]
    public void Remove_PlacedFixture_SetsIsArranged()
    {
        var layout = new StageLayout();
        var id = Guid.NewGuid();
        layout.Place(id, 1, 1);
        layout.IsArranged = false;

        Assert.True(layout.Remove(id));

        Assert.True(layout.IsArranged);
    }

    [Fact]
    public void Resize_NewLayout_SetsIsArranged()
    {
        var layout = new StageLayout();

        layout.Resize(20, 10);

        Assert.True(layout.IsArranged);
    }

    [Fact]
    public void RemoveMissingAndNormalize_LoadedLayout_DoNotSetIsArranged()
    {
        var layout = new StageLayout { Placements = [new StagePlacement { FixtureId = Guid.NewGuid(), X = 500 }] };

        layout.Normalize();
        layout.RemoveMissing(new HashSet<Guid>());

        Assert.False(layout.IsArranged);
    }

    [Fact]
    public void PlaceUnplaced_NothingToPlace_LeavesIsArrangedFalse()
    {
        var layout = new StageLayout();

        Assert.Equal(0, StageArrange.PlaceUnplaced(layout, []));

        Assert.False(layout.IsArranged);
    }

    [Fact]
    public void PlaceUnplaced_FixturesPlaced_SetsIsArranged()
    {
        var layout = new StageLayout();

        StageArrange.PlaceUnplaced(layout, Fixtures(3));

        Assert.True(layout.IsArranged);
    }

    [Fact]
    public void PlaceUnplaced_FixtureListedTwiceAndOneAlreadyPlaced_PlacesEachMissingOnce()
    {
        var layout = new StageLayout();
        var fixtures = Fixtures(3);
        layout.Place(fixtures[0].Id, 1, 1);

        var placed = StageArrange.PlaceUnplaced(layout, [fixtures[0], fixtures[1], fixtures[1], fixtures[2]]);

        Assert.Equal(2, placed);
        Assert.Equal(3, layout.Count);
    }

    [Fact]
    public void ArrangeInLineAndGrid_SetIsArranged()
    {
        var line = new StageLayout();
        var grid = new StageLayout();

        StageArrange.ArrangeInLine(line, Fixtures(2), 1);
        StageArrange.ArrangeInGrid(grid, Fixtures(2), 2);

        Assert.True(line.IsArranged);
        Assert.True(grid.IsArranged);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Clone_Layout_CopiesIsArranged(bool arranged)
    {
        var layout = new StageLayout { IsArranged = arranged };

        Assert.Equal(arranged, layout.Clone().IsArranged);
    }

    [Fact]
    public void Serialize_ArrangedEmptyStage_RoundTripsIsArranged()
    {
        var project = new Project();
        var id = Guid.NewGuid();
        project.Stage.Place(id, 1, 1);
        project.Stage.Remove(id);

        var json = _serializer.Serialize(project);
        var loaded = _serializer.Deserialize(json).Project;

        Assert.Contains("\"isArranged\": true", json, StringComparison.Ordinal);
        Assert.True(loaded.Stage.IsArranged);
        Assert.Equal(0, loaded.Stage.Count);
    }

    [Fact]
    public void Deserialize_OldFileWithPlacementsButNoFlag_IsArrangedFalse()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "schemaVersion": 1,
              "profiles": [],
              "fixtures": [ { "id": "{{id}}", "profileId": "x", "modeName": "m" } ],
              "stage": { "width": 10, "depth": 6, "placements": [ { "fixtureId": "{{id}}", "x": 1, "y": 1 } ] }
            }
            """;

        var stage = _serializer.Deserialize(json).Project.Stage;

        Assert.False(stage.IsArranged);
        Assert.Equal(1, stage.Count);
    }

    [Fact]
    public void Find_PlacementReplacedInListWithSameLength_FindsNewEntry()
    {
        var layout = new StageLayout();
        layout.Place(Guid.NewGuid(), 1, 1);
        var newId = Guid.NewGuid();

        layout.Placements[0] = new StagePlacement { FixtureId = newId };

        Assert.Same(layout.Placements[0], layout.Find(newId));
    }

    [Fact]
    public void Find_OldIdAfterInPlaceEdit_ReturnsNull()
    {
        var layout = new StageLayout();
        var oldId = Guid.NewGuid();
        layout.Place(oldId, 1, 1).FixtureId = Guid.NewGuid();

        Assert.Null(layout.Find(oldId));
    }

    [Fact]
    public void Place_ManyNewFixtures_EachFoundUnderItsId()
    {
        var layout = new StageLayout();
        var ids = Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
        {
            layout.Place(id, 1, 1);
        }

        Assert.Equal(300, layout.Count);
        Assert.All(ids, id => Assert.Equal(id, layout.Find(id)!.FixtureId));
    }
}
