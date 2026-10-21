namespace DmxPro.Core.Projects;

/// <summary>
/// O(1) lookup by key into a public, freely mutated <see cref="List{T}"/> (the project's <c>Fixtures</c> and
/// <c>Profiles</c> are edited directly by many callers and replaced by deserialization), without having to keep an index
/// in sync on every edit.
/// <para>
/// The index maps key → position and is only trusted after checking that the item at that position still has the key.
/// When the index is stale (items added, removed, reordered, replaced or re-keyed) the lookup falls back to the same
/// linear scan as before and rebuilds the index, so the result is always what a <c>FirstOrDefault</c> scan returns for
/// unique keys. A key that is not in the list costs one linear scan without allocating, as before.
/// </para>
/// <para>
/// The index is an immutable snapshot swapped in as a whole, so a reader on another thread never sees a dictionary
/// being filled (the list itself must not be mutated concurrently, which was already the case).
/// </para>
/// </summary>
internal sealed class ListLookup<TKey, TItem>
    where TKey : notnull
    where TItem : class
{
    private readonly Func<TItem, TKey> _keyOf;
    private readonly IEqualityComparer<TKey> _comparer;
    private Snapshot? _snapshot;

    public ListLookup(Func<TItem, TKey> keyOf, IEqualityComparer<TKey>? comparer = null)
    {
        _keyOf = keyOf;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
    }

    /// <summary>The first item of <paramref name="list"/> whose key equals <paramref name="key"/>, or null.</summary>
    public TItem? Find(List<TItem> list, TKey key)
    {
        var snapshot = _snapshot;
        // Keys come from deserialized JSON and can be null despite the annotations: those only take the linear scan.
        if (key is not null &&
            snapshot is not null &&
            ReferenceEquals(snapshot.List, list) &&
            snapshot.Index.TryGetValue(key, out var position) &&
            position < list.Count)
        {
            var candidate = list[position];
            if (candidate is not null && _comparer.Equals(_keyOf(candidate), key))
            {
                return candidate;
            }
        }

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item is not null && _comparer.Equals(_keyOf(item), key))
            {
                // Found by scanning: the index is missing or stale, so rebuild it for the next lookups.
                _snapshot = Build(list);
                return item;
            }
        }

        return null;
    }

    private Snapshot Build(List<TItem> list)
    {
        var index = new Dictionary<TKey, int>(list.Count, _comparer);
        for (var i = 0; i < list.Count; i++)
        {
            // First occurrence wins, like FirstOrDefault. Null items/keys (malformed files) are not indexed.
            if (list[i] is { } item && _keyOf(item) is { } key)
            {
                index.TryAdd(key, i);
            }
        }

        return new Snapshot(list, index);
    }

    private sealed record Snapshot(List<TItem> List, Dictionary<TKey, int> Index);
}
