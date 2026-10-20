using DmxPro.Core.Patching;
using DmxPro.Core.Profiles;
using DmxPro.Core.Projects;
using DmxPro.Core.Stage;

namespace DmxPro.Core.Tests;

/// <summary>Bug hunt: virtual stage layout, auto-arrange and colour model edge cases.</summary>
public class BugHuntStageTests
{
    private static List<Fixture> Fixtures(int count) =>
        Enumerable.Range(0, count).Select(i => new Fixture { Name = $"F{i}", StartAddress = 1 + i }).ToList();

    [Fact]
    public void Find_FixtureIdEditedInPlace_FindsPlacementUnderNewId()
    {
        var layout = new StageLayout();
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var placement = layout.Place(oldId, 1, 1);

        placement.FixtureId = newId;

        // Find(oldId) is the only path that notices the stale index; Find(newId) must work on its own.
        Assert.Same(placement, layout.Find(newId));
    }

    [Fact]
    public void Place_FixtureIdEditedInPlace_DoesNotCreateDuplicatePlacement()
    {
        var layout = new StageLayout();
        var newId = Guid.NewGuid();
        layout.Place(Guid.NewGuid(), 1, 1).FixtureId = newId;

        layout.Place(newId, 5, 5);

        Assert.Equal(1, layout.Count);
    }

    [Fact]
    public void PlaceUnplaced_TinyStageWithManyFixtures_AllInsideAndDistinct()
    {
        var layout = new StageLayout();
        layout.Resize(1, 1);
        var fixtures = Fixtures(500);

        var placed = StageArrange.PlaceUnplaced(layout, fixtures);

        Assert.Equal(500, placed);
        Assert.All(layout.Placements, p => Assert.InRange(p.X, 0, 1));
        Assert.All(layout.Placements, p => Assert.InRange(p.Y, 0, 1));
        Assert.Equal(500, layout.Placements.Select(p => (p.X, p.Y)).Distinct().Count());
    }

    [Fact]
    public void PlaceUnplaced_ExistingPlacementsOfUnknownFixturesOnEdges_NewOnesAvoidThem()
    {
        var layout = new StageLayout();
        layout.Resize(3, 2);
        // Placements on the far edges/corners (X == Width, Y == 0) must still map into a cell.
        layout.Place(Guid.NewGuid(), 3, 0);
        layout.Place(Guid.NewGuid(), 0, 2);
        var fixtures = Fixtures(7);

        StageArrange.PlaceUnplaced(layout, fixtures);

        Assert.Equal(9, layout.Count);
        Assert.Equal(9, layout.Placements.Select(p => (p.X, p.Y)).Distinct().Count());
    }

    [Fact]
    public void RemoveFixture_PlacedAndGrouped_RemovesPlacementAndGroupReference()
    {
        var project = new Project();
        var fixture = project.AddFixtures(TestProfiles.Rgbw(), "4ch", 2, 1, 1, "Par")[0];
        var group = project.CreateGroup("G", [fixture.Id]);
        StageArrange.PlaceUnplaced(project.Stage, project.Fixtures);
        var copy = project.DuplicateFixture(fixture.Id);

        Assert.True(project.RemoveFixture(fixture.Id));

        Assert.Null(project.Stage.Find(fixture.Id));
        Assert.DoesNotContain(fixture.Id, group.FixtureIds);
        Assert.Contains(copy.Id, group.FixtureIds);
        Assert.Null(project.Stage.Find(copy.Id)); // duplicates are not placed automatically
    }

    [Fact]
    public void Evaluate_StrobeNoFunctionRangeAtRest_IsSteady()
    {
        // Typical QLC+ (.qxf) shutter channel: 0–9 "No function", 10–255 strobe. The importer never sets Safe.
        var profile = new FixtureProfile
        {
            Id = "qxf-par",
            Channels =
            [
                new() { Key = "red", Type = ChannelType.Red },
                new()
                {
                    Key = "shutter",
                    Type = ChannelType.Strobe,
                    Capabilities =
                    [
                        new() { Min = 0, Max = 9, Label = "No function" },
                        new() { Min = 10, Max = 255, Label = "Strobe slow to fast" },
                    ],
                },
            ],
            Modes = [new() { Name = "2ch", Channels = ["red", "shutter"] }],
        };
        var model = StageColorModel.Build(profile, profile.Modes[0]);
        var universe = new byte[512];
        universe[0] = 255;

        // Strobe value 0 = the fixture's resting state: the stage must show a steady light, not a flashing one.
        var state = model.Evaluate(universe, 1, 0.5);

        Assert.False(state.IsStrobing);
        Assert.Equal(1f, state.Intensity);
    }

    [Fact]
    public void Evaluate_ImportedSampleQxfStrobeAtZero_IsSteady()
    {
        // The repo's own sample .qxf: Strobe 0–9 "No function", 10–255 "Strobe slow to fast".
        var profile = QxfImporter.ImportFile(Path.Combine(AppContext.BaseDirectory, "TestData", "Sample-Par-RGBW.qxf"));
        var mode = profile.FindMode("8 Channel")!;
        var model = StageColorModel.Build(profile, mode);
        var universe = new byte[512];
        universe[0] = 255; // dimmer
        universe[2] = 255; // red
        // strobe (address 7) = 0: resting value, the real fixture shines steadily.

        var state = model.Evaluate(universe, 1, 0.5);

        Assert.False(state.IsStrobing);
        Assert.Equal(1f, state.Intensity);
    }

    [Fact]
    public void Evaluate_DuplicateRedChannelsAndFine_UsesFirstCoarseRed()
    {
        var profile = new FixtureProfile
        {
            Id = "p",
            Channels =
            [
                new() { Key = "red", Type = ChannelType.Red },
                new() { Key = "red-fine", Type = ChannelType.Fine, CoarseKey = "red" },
                new() { Key = "red2", Type = ChannelType.Red },
            ],
            Modes = [new() { Name = "m", Channels = ["red-fine", "red", "red2"] }],
        };
        var model = StageColorModel.Build(profile, profile.Modes[0]);
        var universe = new byte[512];
        universe[509] = 255; // red-fine at 510
        universe[510] = 128; // red at 511
        universe[511] = 0;   // red2 at 512

        var state = model.Evaluate(universe, 510, 0);

        Assert.Equal(255, state.R);
        Assert.Equal(128 / 255f, state.Intensity, 3);
    }
}
