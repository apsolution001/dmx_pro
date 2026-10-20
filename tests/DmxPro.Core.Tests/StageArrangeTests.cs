using DmxPro.Core.Patching;
using DmxPro.Core.Stage;

namespace DmxPro.Core.Tests;

public class StageArrangeTests
{
    private static List<Fixture> MakeFixtures(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Fixture { Name = $"PAR {i + 1}", Universe = 1 + (i / 100), StartAddress = 1 + ((i % 100) * 5) })
            .ToList();

    private static void AssertInsideStage(StageLayout layout)
    {
        Assert.All(layout.Placements, p =>
        {
            Assert.InRange(p.X, 0, layout.Width);
            Assert.InRange(p.Y, 0, layout.Depth);
        });
    }

    private static void AssertNoOverlap(StageLayout layout)
    {
        var positions = layout.Placements.Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
    }

    [Fact]
    public void PlaceUnplaced_NoFixtures_PlacesNothing()
    {
        var layout = new StageLayout();

        Assert.Equal(0, StageArrange.PlaceUnplaced(layout, []));
        Assert.Empty(layout.Placements);
    }

    [Fact]
    public void PlaceUnplaced_OneFixture_PlacesItCentreStage()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(1);

        Assert.Equal(1, StageArrange.PlaceUnplaced(layout, fixtures));

        var placement = layout.Find(fixtures[0].Id)!;
        Assert.Equal(layout.Width / 2, placement.X, 6);
        Assert.Equal(layout.Depth / 2, placement.Y, 6);
    }

    [Fact]
    public void PlaceUnplaced_FewFixtures_OneEvenlySpacedRowWithMargins()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(4);

        StageArrange.PlaceUnplaced(layout, fixtures);

        var xs = fixtures.Select(f => layout.Find(f.Id)!.X).ToList();
        Assert.Equal([1.5, 4.5, 7.5, 10.5], xs);
        Assert.All(fixtures, f => Assert.Equal(4, layout.Find(f.Id)!.Y, 6));
    }

    [Fact]
    public void PlaceUnplaced_ManyFixtures_AllInsideWithoutOverlap()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(500);

        Assert.Equal(500, StageArrange.PlaceUnplaced(layout, fixtures));

        Assert.Equal(500, layout.Count);
        AssertInsideStage(layout);
        AssertNoOverlap(layout);
    }

    [Fact]
    public void PlaceUnplaced_SeveralRows_BackRowFirstInPatchOrder()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(30);
        var shuffled = fixtures.AsEnumerable().Reverse().ToList();

        StageArrange.PlaceUnplaced(layout, shuffled);

        var first = layout.Find(fixtures[0].Id)!;
        var second = layout.Find(fixtures[1].Id)!;
        var last = layout.Find(fixtures[^1].Id)!;
        Assert.True(first.Y > last.Y, "first fixture in patch order sits in the back row");
        Assert.True(first.X < second.X, "rows run left to right");
        Assert.Equal(first.Y, second.Y, 6);
    }

    [Fact]
    public void PlaceUnplaced_DifferentUniverses_OrdersByUniverseThenAddress()
    {
        var layout = new StageLayout();
        var a = new Fixture { Name = "B", Universe = 2, StartAddress = 1 };
        var b = new Fixture { Name = "A", Universe = 1, StartAddress = 9 };
        var c = new Fixture { Name = "C", Universe = 1, StartAddress = 1 };

        StageArrange.PlaceUnplaced(layout, [a, b, c]);

        Assert.True(layout.Find(c.Id)!.X < layout.Find(b.Id)!.X);
        Assert.True(layout.Find(b.Id)!.X < layout.Find(a.Id)!.X);
    }

    [Fact]
    public void PlaceUnplaced_SameAddress_OrdersByName()
    {
        var layout = new StageLayout();
        var zed = new Fixture { Name = "Zed", StartAddress = 1 };
        var abe = new Fixture { Name = "Abe", StartAddress = 1 };

        StageArrange.PlaceUnplaced(layout, [zed, abe]);

        Assert.True(layout.Find(abe.Id)!.X < layout.Find(zed.Id)!.X);
    }

    [Fact]
    public void PlaceUnplaced_SomeAlreadyPlaced_KeepsThemAndAvoidsTheirCells()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(6);
        layout.Place(fixtures[0].Id, 1.5, 4);
        layout.Place(fixtures[3].Id, 7.7, 2.2);

        var placed = StageArrange.PlaceUnplaced(layout, fixtures);

        Assert.Equal(4, placed);
        Assert.Equal(1.5, layout.Find(fixtures[0].Id)!.X);
        Assert.Equal(4, layout.Find(fixtures[0].Id)!.Y);
        Assert.Equal(7.7, layout.Find(fixtures[3].Id)!.X);
        Assert.Equal(2.2, layout.Find(fixtures[3].Id)!.Y);
        Assert.Equal(6, layout.Count);
        AssertNoOverlap(layout);
    }

    [Fact]
    public void PlaceUnplaced_CalledTwice_SecondCallPlacesNothing()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(10);
        StageArrange.PlaceUnplaced(layout, fixtures);
        var before = layout.Placements.Select(p => (p.X, p.Y)).ToList();

        Assert.Equal(0, StageArrange.PlaceUnplaced(layout, fixtures));

        Assert.Equal(before, layout.Placements.Select(p => (p.X, p.Y)).ToList());
    }

    [Fact]
    public void PlaceUnplaced_ExistingPlacementsStacked_NewOnesDistinctAndInside()
    {
        var layout = new StageLayout();
        foreach (var fixture in MakeFixtures(40))
        {
            layout.Place(fixture.Id, 6, 4);
        }

        var added = MakeFixtures(40);
        Assert.Equal(40, StageArrange.PlaceUnplaced(layout, added));

        var newPositions = added.Select(f => layout.Find(f.Id)!).Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(40, newPositions.Distinct().Count());
        Assert.DoesNotContain((6.0, 4.0), newPositions);
        AssertInsideStage(layout);
    }

    [Fact]
    public void PlaceUnplaced_DuplicateFixtureInList_PlacedOnce()
    {
        var layout = new StageLayout();
        var fixture = MakeFixtures(1)[0];

        Assert.Equal(1, StageArrange.PlaceUnplaced(layout, [fixture, fixture]));
        Assert.Single(layout.Placements);
    }

    [Fact]
    public void ArrangeInLine_Fixtures_EvenlySpacedAtDepthInGivenOrder()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(3);
        layout.Place(fixtures[1].Id, 0, 0);

        StageArrange.ArrangeInLine(layout, [fixtures[2], fixtures[0], fixtures[1]], 2);

        Assert.Equal(2, layout.Find(fixtures[2].Id)!.X, 6);
        Assert.Equal(6, layout.Find(fixtures[0].Id)!.X, 6);
        Assert.Equal(10, layout.Find(fixtures[1].Id)!.X, 6);
        Assert.All(layout.Placements, p => Assert.Equal(2, p.Y));
        Assert.Equal(3, layout.Count);
    }

    [Fact]
    public void ArrangeInLine_DepthOutsideStage_IsClamped()
    {
        var layout = new StageLayout();

        StageArrange.ArrangeInLine(layout, MakeFixtures(2), 100);

        Assert.All(layout.Placements, p => Assert.Equal(layout.Depth, p.Y));
    }

    [Fact]
    public void ArrangeInLine_NoFixtures_DoesNothing()
    {
        var layout = new StageLayout();

        StageArrange.ArrangeInLine(layout, [], 1);

        Assert.Empty(layout.Placements);
    }

    [Fact]
    public void ArrangeInGrid_SixInThreeColumns_TwoRowsBackFirst()
    {
        var layout = new StageLayout();
        var fixtures = MakeFixtures(6);

        StageArrange.ArrangeInGrid(layout, fixtures, 3);

        var positions = fixtures.Select(f => layout.Find(f.Id)!).Select(p => (p.X, p.Y)).ToList();
        Assert.Equal([(2.0, 6.0), (6.0, 6.0), (10.0, 6.0), (2.0, 2.0), (6.0, 2.0), (10.0, 2.0)], positions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(1000)]
    public void ArrangeInGrid_ColumnsOutOfRange_ClampedAndNoOverlap(int columns)
    {
        var layout = new StageLayout();

        StageArrange.ArrangeInGrid(layout, MakeFixtures(7), columns);

        Assert.Equal(7, layout.Count);
        AssertInsideStage(layout);
        AssertNoOverlap(layout);
    }

    [Fact]
    public void ArrangeInGrid_ManyFixtures_AllInsideWithoutOverlap()
    {
        var layout = new StageLayout();

        StageArrange.ArrangeInGrid(layout, MakeFixtures(500), 25);

        Assert.Equal(500, layout.Count);
        AssertInsideStage(layout);
        AssertNoOverlap(layout);
    }
}
