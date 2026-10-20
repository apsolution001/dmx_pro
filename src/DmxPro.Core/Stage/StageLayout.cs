using System.Text.Json.Serialization;

namespace DmxPro.Core.Stage;

/// <summary>
/// The project's virtual stage: its size and where each fixture sits. Saved with the project. Fixtures without a
/// placement are not drawn until placed (see <see cref="StageArrange"/>).
/// </summary>
/// <remarks>
/// Edit placements through <see cref="Place"/>, <see cref="Remove"/> and <see cref="RemoveMissing"/>: they keep the
/// fixture-id index used by <see cref="Find"/> current. Direct changes to <see cref="Placements"/> or to a placement's
/// <see cref="StagePlacement.FixtureId"/> are tolerated: a hit is checked against the placement's current id and a
/// miss falls back to a scan of the list (then the index is rebuilt), so <see cref="Find"/> never returns a stale or
/// missing answer.
/// </remarks>
public sealed class StageLayout
{
    /// <summary>Smallest allowed stage width/depth in metres.</summary>
    public const double MinSize = 1;

    /// <summary>Largest allowed stage width/depth in metres.</summary>
    public const double MaxSize = 200;

    /// <summary>Default stage width in metres.</summary>
    public const double DefaultWidth = 12;

    /// <summary>Default stage depth in metres.</summary>
    public const double DefaultDepth = 8;

    private double _width = DefaultWidth;
    private double _depth = DefaultDepth;
    private List<StagePlacement> _placements = [];

    // Lazily rebuilt fixture id -> placement index. Null = must be rebuilt.
    private Dictionary<Guid, StagePlacement>? _index;
    private int _indexedCount;

    /// <summary>
    /// Stage width in metres (left–right). Default 12. Kept within <see cref="MinSize"/>..<see cref="MaxSize"/>; a
    /// non-finite value (bad file) falls back to the default. Use <see cref="Resize"/> to also pull placements inside.
    /// </summary>
    public double Width
    {
        get => _width;
        set => _width = SanitizeSize(value, DefaultWidth);
    }

    /// <summary>
    /// Stage depth in metres (front–back). Default 8. Kept within <see cref="MinSize"/>..<see cref="MaxSize"/>; a
    /// non-finite value (bad file) falls back to the default. Use <see cref="Resize"/> to also pull placements inside.
    /// </summary>
    public double Depth
    {
        get => _depth;
        set => _depth = SanitizeSize(value, DefaultDepth);
    }

    /// <summary>One entry per placed fixture. Never null (a JSON <c>null</c> becomes an empty list).</summary>
    public List<StagePlacement> Placements
    {
        get => _placements;
        set
        {
            _placements = value ?? [];
            _index = null;
        }
    }

    /// <summary>
    /// True once the stage was arranged (fixtures placed, moved or removed, or the stage resized) — by the user or by an
    /// automatic placement the user then saved. False for a new project and for files saved before this flag existed;
    /// the Stage page uses it to tell "never arranged" (auto-place, without marking the project changed) from "the
    /// owner's layout" (respect it, even when empty). Set by <see cref="Place"/>, <see cref="Remove"/>,
    /// <see cref="Resize"/> and <see cref="StageArrange"/>; never by loading or repairing a file.
    /// </summary>
    public bool IsArranged { get; set; }

    /// <summary>Number of placed fixtures.</summary>
    [JsonIgnore]
    public int Count => _placements.Count;

    /// <summary>The placement of a fixture, or null when it is not on the stage.</summary>
    public StagePlacement? Find(Guid fixtureId)
    {
        if (EnsureIndex().TryGetValue(fixtureId, out var placement) && placement.FixtureId == fixtureId)
        {
            return placement;
        }

        // Miss or stale hit: a placement's FixtureId may have been edited in place, which the index cannot see. Scan
        // the list (no allocation) and rebuild the index only when the scan proves it stale, so a plain miss stays cheap.
        var stale = placement is not null;
        StagePlacement? found = null;
        foreach (var candidate in _placements)
        {
            if (candidate is not null && candidate.FixtureId == fixtureId)
            {
                found = candidate;
                stale = true;
                break;
            }
        }

        if (stale)
        {
            _index = null;
            EnsureIndex();
        }

        return found;
    }

    /// <summary>True when the fixture has a placement.</summary>
    public bool IsPlaced(Guid fixtureId) => Find(fixtureId) is not null;

    /// <summary>
    /// Places (or moves) a fixture; the position is clamped to the stage. A new placement is added to the index
    /// directly (no rebuild), so placing many fixtures stays linear. Sets <see cref="IsArranged"/>.
    /// </summary>
    public StagePlacement Place(Guid fixtureId, double x, double y)
    {
        var placement = Find(fixtureId);
        if (placement is null)
        {
            placement = new StagePlacement { FixtureId = fixtureId };
            var index = EnsureIndex();
            _placements.Add(placement);
            index[fixtureId] = placement;
            _indexedCount = _placements.Count;
        }

        placement.X = ClampCoordinate(x, _width);
        placement.Y = ClampCoordinate(y, _depth);
        IsArranged = true;
        return placement;
    }

    /// <summary>
    /// Takes a fixture off the stage. Returns false (and changes nothing) when it was not placed; otherwise sets
    /// <see cref="IsArranged"/>.
    /// </summary>
    public bool Remove(Guid fixtureId)
    {
        if (Find(fixtureId) is null)
        {
            return false;
        }

        _placements.RemoveAll(p => p is not null && p.FixtureId == fixtureId);
        _index = null;
        IsArranged = true;
        return true;
    }

    /// <summary>
    /// Drops placements of fixtures that no longer exist (file repair, not an arrangement: <see cref="IsArranged"/> is
    /// left alone). Returns how many were removed.
    /// </summary>
    public int RemoveMissing(IReadOnlySet<Guid> existingFixtureIds)
    {
        ArgumentNullException.ThrowIfNull(existingFixtureIds);
        var removed = _placements.RemoveAll(p => !existingFixtureIds.Contains(p.FixtureId));
        if (removed > 0)
        {
            _index = null;
        }

        return removed;
    }

    /// <summary>
    /// Changes the stage size (clamped to <see cref="MinSize"/>..<see cref="MaxSize"/>) and pulls every placement back
    /// inside the new size. Sets <see cref="IsArranged"/>.
    /// </summary>
    public void Resize(double width, double depth)
    {
        Width = width;
        Depth = depth;
        IsArranged = true;
        foreach (var placement in _placements)
        {
            placement.X = ClampCoordinate(placement.X, _width);
            placement.Y = ClampCoordinate(placement.Y, _depth);
        }
    }

    /// <summary>
    /// Repairs a loaded layout instead of rejecting the file: drops null entries and duplicate fixture ids (the first
    /// wins), clamps positions to the stage and replaces non-finite rotations with 0. Returns how many entries were
    /// dropped.
    /// </summary>
    public int Normalize()
    {
        var seen = new HashSet<Guid>();
        var removed = _placements.RemoveAll(p => p is null || !seen.Add(p.FixtureId));
        foreach (var placement in _placements)
        {
            placement.X = ClampCoordinate(placement.X, _width);
            placement.Y = ClampCoordinate(placement.Y, _depth);
            if (!double.IsFinite(placement.Rotation))
            {
                placement.Rotation = 0;
            }
        }

        _index = null;
        return removed;
    }

    /// <summary>Deep copy.</summary>
    public StageLayout Clone()
    {
        var copy = new StageLayout { _width = _width, _depth = _depth, IsArranged = IsArranged };
        copy._placements = new List<StagePlacement>(_placements.Count);
        foreach (var placement in _placements)
        {
            copy._placements.Add(placement.Clone());
        }

        return copy;
    }

    /// <summary>Clamps a coordinate to 0..<paramref name="max"/>; non-finite values become 0.</summary>
    internal static double ClampCoordinate(double value, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, max) : 0;

    private static double SanitizeSize(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, MinSize, MaxSize) : fallback;

    private Dictionary<Guid, StagePlacement> EnsureIndex()
    {
        if (_index is not null && _indexedCount == _placements.Count)
        {
            return _index;
        }

        var index = new Dictionary<Guid, StagePlacement>(_placements.Count);
        foreach (var placement in _placements)
        {
            if (placement is not null)
            {
                index.TryAdd(placement.FixtureId, placement);
            }
        }

        _index = index;
        _indexedCount = _placements.Count;
        return index;
    }
}
