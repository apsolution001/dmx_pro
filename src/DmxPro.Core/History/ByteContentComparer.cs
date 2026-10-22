namespace DmxPro.Core.History;

/// <summary>Compares byte arrays by content (for matching snapshot items by their JSON).</summary>
internal sealed class ByteContentComparer : IEqualityComparer<byte[]>
{
    public static readonly ByteContentComparer Instance = new();

    public bool Equals(byte[]? x, byte[]? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

    public int GetHashCode(byte[] obj)
    {
        var hash = default(HashCode);
        hash.AddBytes(obj);
        return hash.ToHashCode();
    }
}
