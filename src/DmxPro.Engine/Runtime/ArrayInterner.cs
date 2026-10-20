using System.Runtime.InteropServices;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// Shares equal immutable arrays between compiled programs. Every show item plays its own copy of a function, so a
/// project with five long shows holds thousands of scenes with identical channel/value lists; compiled one by one they
/// would cost megabytes per recompile and stay alive in the engine model. Compile time only (allocates freely).
/// </summary>
/// <typeparam name="T">Unmanaged element type (compared and hashed by content).</typeparam>
internal sealed class ArrayInterner<T>
    where T : unmanaged, IEquatable<T>
{
    // Content hash → arrays with that hash (almost always one).
    private readonly Dictionary<int, List<T[]>> _pool = [];

    /// <summary>
    /// Returns a previously interned array with the same content as <paramref name="content"/>, or a new array holding
    /// a copy of it (remembered for later calls). The returned array must never be modified.
    /// </summary>
    public T[] Intern(ReadOnlySpan<T> content)
    {
        if (content.IsEmpty)
        {
            return [];
        }

        var hash = default(HashCode);
        hash.AddBytes(MemoryMarshal.AsBytes(content));
        var key = hash.ToHashCode();
        if (_pool.TryGetValue(key, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (content.SequenceEqual(candidate))
                {
                    return candidate;
                }
            }
        }
        else
        {
            candidates = new List<T[]>(1);
            _pool.Add(key, candidates);
        }

        var array = content.ToArray();
        candidates.Add(array);
        return array;
    }
}
