namespace DmxPro.Core.History;

/// <summary>
/// One list of a <see cref="ProjectSnapshot"/> (fixtures, functions, …): per item its key (id), display name and compact
/// JSON. Items that did not change since the previous snapshot share the previous byte array, and a list that did not
/// change at all shares the whole section, so a history of 100 steps costs little more than one copy of the project.
/// </summary>
internal sealed class SnapshotSection
{
    public static readonly SnapshotSection Empty = new([], [], []);

    public SnapshotSection(string[] keys, string[] names, byte[][] items)
    {
        Keys = keys;
        Names = names;
        Items = items;
    }

    public string[] Keys { get; }

    public string[] Names { get; }

    public byte[][] Items { get; }

    public int Count => Items.Length;

    /// <summary>Same items in the same order (cheap: unchanged items share their arrays).</summary>
    public bool ContentEquals(SnapshotSection other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Items.Length != other.Items.Length)
        {
            return false;
        }

        for (var i = 0; i < Items.Length; i++)
        {
            if (!SameBytes(Items[i], other.Items[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Index of the item with this key, or -1.</summary>
    public int IndexOfKey(string key)
    {
        for (var i = 0; i < Keys.Length; i++)
        {
            if (string.Equals(Keys[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool SameBytes(byte[] a, byte[] b) => ReferenceEquals(a, b) || a.AsSpan().SequenceEqual(b);
}
