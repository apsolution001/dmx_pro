namespace DmxPro.Engine.Runtime;

/// <summary>A compiled show: its playable items flattened and sorted by start time.</summary>
internal sealed class ShowProgram
{
    // _maxEndUpTo[i] = latest End of items[0..i]; non-decreasing, so binary search finds the first item that can still
    // be live. Lets a long show render only the items around the playhead instead of scanning all of them every tick.
    private readonly double[] _maxEndUpTo;

    /// <param name="id">Show id.</param>
    /// <param name="lengthSeconds">Show length.</param>
    /// <param name="items">Items sorted by <see cref="ShowItemProgram.Start"/> (ascending).</param>
    public ShowProgram(Guid id, double lengthSeconds, ShowItemProgram[] items)
    {
        Id = id;
        LengthSeconds = lengthSeconds;
        Items = items;
        _maxEndUpTo = new double[items.Length];
        var maxEnd = double.NegativeInfinity;
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0 && items[i].Start < items[i - 1].Start)
            {
                throw new ArgumentException("Show items must be sorted by start time.", nameof(items));
            }

            maxEnd = Math.Max(maxEnd, items[i].End);
            _maxEndUpTo[i] = maxEnd;
        }
    }

    public Guid Id { get; }

    public double LengthSeconds { get; }

    public ShowItemProgram[] Items { get; }

    /// <summary>
    /// Index of the first item that may be live at <paramref name="position"/>: every earlier item has already ended
    /// (End ≤ position). Items from here on are live until one starts after the position. O(log n), no allocation.
    /// </summary>
    public int FirstLiveIndex(double position)
    {
        int low = 0, high = _maxEndUpTo.Length;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (_maxEndUpTo[mid] <= position)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
