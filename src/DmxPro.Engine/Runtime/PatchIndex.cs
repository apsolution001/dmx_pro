namespace DmxPro.Engine.Runtime;

/// <summary>
/// Compile-time lookup "fixture + channel key → flat channel index". Two levels on purpose: one <see cref="Guid"/>
/// lookup per fixture and then an ordinal scan of its few channel keys, instead of hashing a (Guid, string) tuple per
/// scene value — compiling a project with thousands of show copies does millions of these lookups on the UI thread.
/// Built by <see cref="EngineModelBuilder"/>; never used by the tick.
/// </summary>
internal sealed class PatchIndex
{
    private readonly Dictionary<Guid, FixtureChannels> _fixtures = [];

    /// <summary>Number of patched fixtures.</summary>
    public int FixtureCount => _fixtures.Count;

    /// <summary>Records (or, for a repeated key, replaces) where a fixture channel is patched.</summary>
    public void Add(Guid fixtureId, string channelKey, int flat)
    {
        if (!_fixtures.TryGetValue(fixtureId, out var channels))
        {
            channels = new FixtureChannels();
            _fixtures.Add(fixtureId, channels);
        }

        channels.Set(channelKey, flat);
    }

    /// <summary>The patched channels of a fixture, or null when it is not patched (missing, disabled, unresolved).</summary>
    public FixtureChannels? Find(Guid fixtureId) => _fixtures.TryGetValue(fixtureId, out var channels) ? channels : null;

    /// <summary>Flat index of a fixture channel.</summary>
    public bool TryGetFlat(Guid fixtureId, string channelKey, out int flat)
    {
        if (_fixtures.TryGetValue(fixtureId, out var channels))
        {
            return channels.TryGetFlat(channelKey, out flat);
        }

        flat = -1;
        return false;
    }

    /// <summary>One fixture's channel keys and their flat indexes (a mode has few channels: linear scan beats hashing).</summary>
    internal sealed class FixtureChannels
    {
        private string[] _keys = new string[8];
        private int[] _flats = new int[8];
        private int _count;

        public bool TryGetFlat(string channelKey, out int flat)
        {
            for (var i = 0; i < _count; i++)
            {
                if (string.Equals(_keys[i], channelKey, StringComparison.Ordinal))
                {
                    flat = _flats[i];
                    return true;
                }
            }

            flat = -1;
            return false;
        }

        public void Set(string channelKey, int flat)
        {
            for (var i = 0; i < _count; i++)
            {
                if (string.Equals(_keys[i], channelKey, StringComparison.Ordinal))
                {
                    _flats[i] = flat;
                    return;
                }
            }

            if (_count == _keys.Length)
            {
                Array.Resize(ref _keys, _count * 2);
                Array.Resize(ref _flats, _count * 2);
            }

            _keys[_count] = channelKey;
            _flats[_count] = flat;
            _count++;
        }
    }
}
