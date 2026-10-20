using DmxPro.Core.Patching;

namespace DmxPro.Core.Stage;

/// <summary>Automatic placement helpers for the virtual stage.</summary>
/// <remarks>
/// All layouts use the same cell scheme: the stage is split into equal cells and each fixture sits at the centre of its
/// cell, so the margin to the stage edge is half the spacing between neighbours. Rows are filled from the back of the
/// stage (largest Y) towards the audience, left to right.
/// </remarks>
public static class StageArrange
{
    /// <summary>
    /// Preferred spacing between neighbours in a row, in metres. Rows hold at least stage-width / this many fixtures,
    /// so a typical rig (a dozen PARs) ends up in one or two rows like a truss, not in a square block.
    /// </summary>
    public const double PreferredSpacing = 1.0;

    /// <summary>
    /// Places every given fixture that has no placement yet, in patch order, in rows across the stage (evenly spaced,
    /// starting at the back row); already placed fixtures are not moved. Returns the number placed. Sets
    /// <see cref="StageLayout.IsArranged"/> only when something was placed.
    /// </summary>
    /// <remarks>
    /// Patch order is universe, start address, then name. The grid is sized for all placements plus the new fixtures;
    /// cells that already contain a placement are skipped, so a new fixture never lands in an occupied cell.
    /// </remarks>
    public static int PlaceUnplaced(StageLayout layout, IReadOnlyList<Fixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(fixtures);

        // One set of placed ids instead of a Find per fixture: a Find miss scans the placement list.
        var seen = new HashSet<Guid>();
        foreach (var placement in layout.Placements)
        {
            if (placement is not null)
            {
                seen.Add(placement.FixtureId);
            }
        }

        var pending = new List<Fixture>();
        foreach (var fixture in fixtures)
        {
            if (fixture is not null && seen.Add(fixture.Id))
            {
                pending.Add(fixture);
            }
        }

        if (pending.Count == 0)
        {
            return 0;
        }

        SortInPatchOrder(pending);

        var total = layout.Count + pending.Count;
        var (columns, rows) = RowGrid(layout.Width, layout.Depth, total);
        var cellWidth = layout.Width / columns;
        var cellDepth = layout.Depth / rows;

        // Mark the cells already holding a placement. Every placement lies in exactly one cell and there are at least
        // `total` cells, so at least pending.Count cells stay free.
        var occupied = new bool[columns * rows];
        foreach (var placement in layout.Placements)
        {
            if (placement is null)
            {
                continue;
            }

            var column = Math.Clamp((int)(placement.X / cellWidth), 0, columns - 1);
            var rowFromBack = Math.Clamp((int)((layout.Depth - placement.Y) / cellDepth), 0, rows - 1);
            occupied[(rowFromBack * columns) + column] = true;
        }

        var cell = 0;
        foreach (var fixture in pending)
        {
            while (occupied[cell])
            {
                cell++;
            }

            var (x, y) = CellCentre(layout, columns, rows, cell);
            layout.Place(fixture.Id, x, y);
            cell++;
        }

        return pending.Count;
    }

    /// <summary>
    /// Re-places the given fixtures evenly in one straight line across the stage at depth <paramref name="y"/> (clamped
    /// to the stage), in the given order (left to right). Repeated fixtures are placed once. Sets
    /// <see cref="StageLayout.IsArranged"/>.
    /// </summary>
    public static void ArrangeInLine(StageLayout layout, IReadOnlyList<Fixture> fixtures, double y)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var distinct = Distinct(fixtures);
        layout.IsArranged = true;
        for (var i = 0; i < distinct.Count; i++)
        {
            layout.Place(distinct[i].Id, layout.Width * (i + 0.5) / distinct.Count, y);
        }
    }

    /// <summary>
    /// Re-places the given fixtures in an evenly spaced grid with <paramref name="columns"/> columns, in the given order
    /// (back row first, left to right). <paramref name="columns"/> is clamped to 1..fixture count. Repeated fixtures are
    /// placed once. Sets <see cref="StageLayout.IsArranged"/>.
    /// </summary>
    public static void ArrangeInGrid(StageLayout layout, IReadOnlyList<Fixture> fixtures, int columns)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var distinct = Distinct(fixtures);
        layout.IsArranged = true;
        if (distinct.Count == 0)
        {
            return;
        }

        var cols = Math.Clamp(columns, 1, distinct.Count);
        var rows = (distinct.Count + cols - 1) / cols;
        for (var i = 0; i < distinct.Count; i++)
        {
            var (x, y) = CellCentre(layout, cols, rows, i);
            layout.Place(distinct[i].Id, x, y);
        }
    }

    /// <summary>
    /// Columns and rows for <paramref name="count"/> fixtures: wide rows (at least one fixture per
    /// <see cref="PreferredSpacing"/> of width) but roughly square cells once the stage gets crowded, then balanced so
    /// the rows are evenly filled.
    /// </summary>
    private static (int Columns, int Rows) RowGrid(double width, double depth, int count)
    {
        var byWidth = (int)Math.Floor(width / PreferredSpacing);
        var bySquare = (int)Math.Ceiling(Math.Sqrt(count * width / depth));
        var columns = Math.Clamp(Math.Max(byWidth, bySquare), 1, count);
        var rows = (count + columns - 1) / columns;
        columns = (count + rows - 1) / rows;
        return (columns, rows);
    }

    private static (double X, double Y) CellCentre(StageLayout layout, int columns, int rows, int cell)
    {
        var column = cell % columns;
        var rowFromBack = cell / columns;
        var x = layout.Width * (column + 0.5) / columns;
        var y = layout.Depth - (layout.Depth * (rowFromBack + 0.5) / rows);
        return (x, y);
    }

    private static void SortInPatchOrder(List<Fixture> fixtures)
    {
        // List.Sort is unstable; the original position breaks remaining ties so the result is deterministic.
        var position = new Dictionary<Guid, int>(fixtures.Count);
        for (var i = 0; i < fixtures.Count; i++)
        {
            position[fixtures[i].Id] = i;
        }

        fixtures.Sort((a, b) =>
        {
            var result = a.Universe.CompareTo(b.Universe);
            if (result == 0)
            {
                result = a.StartAddress.CompareTo(b.StartAddress);
            }

            if (result == 0)
            {
                result = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            }

            return result != 0 ? result : position[a.Id].CompareTo(position[b.Id]);
        });
    }

    private static List<Fixture> Distinct(IReadOnlyList<Fixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        var result = new List<Fixture>(fixtures.Count);
        var seen = new HashSet<Guid>();
        foreach (var fixture in fixtures)
        {
            if (fixture is not null && seen.Add(fixture.Id))
            {
                result.Add(fixture);
            }
        }

        return result;
    }
}
