using DmxPro.Core.Stage;

namespace DmxPro.Core.Tests;

public class StageLayoutTests
{
    [Fact]
    public void Place_NewFixture_AddsPlacement()
    {
        var layout = new StageLayout();
        var id = Guid.NewGuid();

        var placement = layout.Place(id, 3, 4);

        Assert.Same(placement, Assert.Single(layout.Placements));
        Assert.Equal(id, placement.FixtureId);
        Assert.Equal(3, placement.X);
        Assert.Equal(4, placement.Y);
    }

    [Fact]
    public void Place_AlreadyPlaced_UpdatesExistingPlacement()
    {
        var layout = new StageLayout();
        var id = Guid.NewGuid();
        var first = layout.Place(id, 1, 1);
        first.Rotation = 45;

        var second = layout.Place(id, 5, 6);

        Assert.Same(first, second);
        Assert.Single(layout.Placements);
        Assert.Equal(5, second.X);
        Assert.Equal(6, second.Y);
        Assert.Equal(45, second.Rotation);
    }

    [Theory]
    [InlineData(-3, -1, 0, 0)]
    [InlineData(99, 99, 12, 8)]
    [InlineData(double.NaN, double.PositiveInfinity, 0, 0)]
    public void Place_OutsideStage_ClampsPosition(double x, double y, double expectedX, double expectedY)
    {
        var layout = new StageLayout();

        var placement = layout.Place(Guid.NewGuid(), x, y);

        Assert.Equal(expectedX, placement.X);
        Assert.Equal(expectedY, placement.Y);
    }

    [Theory]
    [InlineData(0, StageLayout.MinSize)]
    [InlineData(-5, StageLayout.MinSize)]
    [InlineData(500, StageLayout.MaxSize)]
    [InlineData(20, 20)]
    [InlineData(double.NaN, StageLayout.DefaultWidth)]
    public void Width_OutOfRange_IsClamped(double value, double expected)
    {
        var layout = new StageLayout { Width = value };

        Assert.Equal(expected, layout.Width);
    }

    [Fact]
    public void Depth_OutOfRange_IsClamped()
    {
        var layout = new StageLayout { Depth = 1000 };

        Assert.Equal(StageLayout.MaxSize, layout.Depth);
    }

    [Fact]
    public void Resize_Smaller_PullsPlacementsInside()
    {
        var layout = new StageLayout();
        var placement = layout.Place(Guid.NewGuid(), 11, 7);

        layout.Resize(6, 4);

        Assert.Equal(6, layout.Width);
        Assert.Equal(4, layout.Depth);
        Assert.Equal(6, placement.X);
        Assert.Equal(4, placement.Y);
    }

    [Fact]
    public void Find_NotPlaced_ReturnsNull()
    {
        var layout = new StageLayout();
        layout.Place(Guid.NewGuid(), 1, 1);

        Assert.Null(layout.Find(Guid.NewGuid()));
    }

    [Fact]
    public void Find_PlacementAddedDirectlyToList_IsFound()
    {
        var layout = new StageLayout();
        layout.Place(Guid.NewGuid(), 1, 1);
        var id = Guid.NewGuid();

        layout.Placements.Add(new StagePlacement { FixtureId = id, X = 2 });

        Assert.Equal(2, layout.Find(id)!.X);
    }

    [Fact]
    public void Find_ManyFixtures_FindsEach()
    {
        var layout = new StageLayout();
        var ids = Enumerable.Range(0, 600).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            layout.Place(id, 1, 1);
        }

        Assert.All(ids, id => Assert.Equal(id, layout.Find(id)!.FixtureId));
        Assert.Equal(600, layout.Count);
    }

    [Fact]
    public void Remove_Placed_RemovesAndReturnsTrue()
    {
        var layout = new StageLayout();
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        layout.Place(id, 1, 1);
        layout.Place(other, 2, 2);

        Assert.True(layout.Remove(id));

        Assert.Null(layout.Find(id));
        Assert.NotNull(layout.Find(other));
        Assert.Single(layout.Placements);
    }

    [Fact]
    public void Remove_NotPlaced_ReturnsFalse()
    {
        var layout = new StageLayout();

        Assert.False(layout.Remove(Guid.NewGuid()));
    }

    [Fact]
    public void Remove_ThenPlaceAgain_AddsFreshPlacement()
    {
        var layout = new StageLayout();
        var id = Guid.NewGuid();
        layout.Place(id, 1, 1);
        layout.Remove(id);

        layout.Place(id, 3, 3);

        Assert.Equal(3, Assert.Single(layout.Placements).X);
    }

    [Fact]
    public void RemoveMissing_SomeGone_DropsOnlyThose()
    {
        var layout = new StageLayout();
        var kept = Guid.NewGuid();
        var gone1 = Guid.NewGuid();
        var gone2 = Guid.NewGuid();
        layout.Place(kept, 1, 1);
        layout.Place(gone1, 2, 2);
        layout.Place(gone2, 3, 3);

        var removed = layout.RemoveMissing(new HashSet<Guid> { kept });

        Assert.Equal(2, removed);
        Assert.Equal(kept, Assert.Single(layout.Placements).FixtureId);
        Assert.Null(layout.Find(gone1));
    }

    [Fact]
    public void Normalize_BadData_RepairsWithoutThrowing()
    {
        var id = Guid.NewGuid();
        var layout = new StageLayout
        {
            Placements =
            [
                new StagePlacement { FixtureId = id, X = -4, Y = 50, Rotation = double.NaN },
                new StagePlacement { FixtureId = id, X = 1, Y = 1 },
            ],
        };

        var removed = layout.Normalize();

        Assert.Equal(1, removed);
        var placement = Assert.Single(layout.Placements);
        Assert.Equal(0, placement.X);
        Assert.Equal(layout.Depth, placement.Y);
        Assert.Equal(0, placement.Rotation);
        Assert.Same(placement, layout.Find(id));
    }

    [Fact]
    public void Placements_SetNull_BecomesEmptyList()
    {
        var layout = new StageLayout { Placements = null! };

        Assert.NotNull(layout.Placements);
        Assert.Empty(layout.Placements);
    }

    [Fact]
    public void Clone_Modified_OriginalUnchanged()
    {
        var layout = new StageLayout { Width = 20, Depth = 10 };
        var id = Guid.NewGuid();
        layout.Place(id, 4, 5).Rotation = 90;

        var copy = layout.Clone();
        copy.Place(id, 9, 9);
        copy.Find(id)!.Rotation = 0;
        copy.Place(Guid.NewGuid(), 1, 1);
        copy.Width = 5;

        Assert.Equal(20, layout.Width);
        Assert.Equal(10, layout.Depth);
        var original = Assert.Single(layout.Placements);
        Assert.Equal(4, original.X);
        Assert.Equal(5, original.Y);
        Assert.Equal(90, original.Rotation);
        Assert.Equal(2, copy.Placements.Count);
        Assert.Equal(9, copy.Find(id)!.X);
    }
}
