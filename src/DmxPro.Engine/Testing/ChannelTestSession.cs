namespace DmxPro.Engine.Testing;

/// <summary>
/// The Test &amp; Preview page's manual channel values. Each test value is keyed by fixture + profile channel key and
/// resolved to a universe/address through the engine patch (profile + mode, never fixed offsets), then sent as an engine
/// manual override. Grand Master, Blackout and strobe safety still apply on top (see <see cref="DmxEngine"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it clears.</b> The engine keeps one override per address. Releasing a channel clears the override at its
/// address (or re-sends another tracked channel sharing that address), even if something else had also set that address
/// meanwhile; addresses this session never set are left alone.
/// </para>
/// <para>
/// <b>Engine-wide clears.</b> Overrides recorded before the latest <see cref="DmxEngine.ReleaseGeneration"/> (engine
/// Release All, ClearOverrides, project load — from any caller) are forgotten by <see cref="Sync"/>, which every
/// mutating member calls first, so <see cref="Reapply"/> never resurrects them.
/// </para>
/// <para>
/// <b>Threading.</b> Not thread-safe: call every member from the UI thread only. The engine calls it makes only queue
/// commands, so nothing here blocks or touches the engine tick.
/// </para>
/// </remarks>
public sealed class ChannelTestSession
{
    private readonly DmxEngine _engine;
    private readonly Dictionary<(Guid FixtureId, string ChannelKey), TestOverride> _overrides = [];
    private long _sequence;
    private long _seenGeneration;

    /// <summary>Creates an active session that sends its overrides to <paramref name="engine"/>.</summary>
    public ChannelTestSession(DmxEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _seenGeneration = engine.ReleaseGeneration;
    }

    /// <summary>Number of fixture channels currently under test.</summary>
    public int Count => _overrides.Count;

    /// <summary>
    /// While false, <see cref="Set(Guid, string, byte)"/> does nothing and returns false (the Test page is not visible). See
    /// <see cref="Activate"/> / <see cref="Deactivate"/>.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The channels under test (unordered). The struct enumerator does not allocate.</summary>
    public Dictionary<(Guid FixtureId, string ChannelKey), TestOverride>.ValueCollection Overrides => _overrides.Values;

    /// <summary>Allows <see cref="Set(Guid, string, byte)"/> again.</summary>
    public void Activate() => IsActive = true;

    /// <summary>Releases everything this session set and refuses further <see cref="Set(Guid, string, byte)"/> calls until activated.</summary>
    public int Deactivate()
    {
        IsActive = false;
        return ReleaseAll();
    }

    /// <summary>
    /// Sets a test value on one fixture channel. Returns false (and changes nothing) when the session is inactive or the
    /// channel is not patched: unknown fixture/key, disabled fixture, missing profile/mode or past address 512.
    /// </summary>
    /// <param name="fixtureId">Fixture id.</param>
    /// <param name="channelKey">Profile channel key.</param>
    /// <param name="value">Raw DMX value (a byte, so always 0–255).</param>
    public bool Set(Guid fixtureId, string channelKey, byte value)
    {
        ArgumentNullException.ThrowIfNull(channelKey);
        if (!IsActive || !_engine.TryResolveChannel(fixtureId, channelKey, out var channel))
        {
            return false;
        }

        Sync();
        var key = (fixtureId, channelKey);
        if (_overrides.TryGetValue(key, out var previous)
            && (previous.Universe != channel.Universe || previous.Address != channel.Address))
        {
            // Stale address (patch changed without Reapply): release the old one first.
            _overrides.Remove(key);
            ReleaseAddress(previous.Universe, previous.Address);
        }

        var generation = _engine.ReleaseGeneration;
        _overrides[key] = new TestOverride(fixtureId, channelKey, channel.Universe, channel.Address, value, ++_sequence, generation);
        _engine.SetChannelOverride(channel.Universe, channel.Address, value);
        return true;
    }

    /// <summary>
    /// Sets a test value from a UI number: rounded half away from zero and clamped to 0–255 (NaN = 0).
    /// </summary>
    public bool Set(Guid fixtureId, string channelKey, double value) => Set(fixtureId, channelKey, ToByte(value));

    /// <summary>
    /// Releases one channel. When another tracked channel (an overlapping fixture) shares the address, the latest of
    /// those is re-sent instead of clearing the address. Returns false when the channel was not under test.
    /// </summary>
    public bool Release(Guid fixtureId, string channelKey)
    {
        ArgumentNullException.ThrowIfNull(channelKey);
        Sync();
        if (!_overrides.Remove((fixtureId, channelKey), out var removed))
        {
            return false;
        }

        ReleaseAddress(removed.Universe, removed.Address);
        return true;
    }

    /// <summary>Releases every channel of one fixture. Returns the number of channels released.</summary>
    public int ReleaseFixture(Guid fixtureId)
    {
        Sync();
        List<TestOverride>? removed = null;
        foreach (var entry in _overrides.Values)
        {
            if (entry.FixtureId == fixtureId)
            {
                (removed ??= []).Add(entry);
            }
        }

        if (removed is null)
        {
            return 0;
        }

        foreach (var entry in removed)
        {
            _overrides.Remove((entry.FixtureId, entry.ChannelKey));
        }

        foreach (var entry in removed)
        {
            ReleaseAddress(entry.Universe, entry.Address);
        }

        return removed.Count;
    }

    /// <summary>Releases every channel this session set. Returns the number of channels released.</summary>
    public int ReleaseAll()
    {
        Sync();
        var count = _overrides.Count;
        if (count == 0)
        {
            return 0;
        }

        var addresses = new HashSet<(int, int)>();
        foreach (var entry in _overrides.Values)
        {
            addresses.Add((entry.Universe, entry.Address));
        }

        _overrides.Clear();
        foreach (var (universe, address) in addresses)
        {
            _engine.ClearChannelOverride(universe, address);
        }

        return count;
    }

    /// <summary>True when the fixture channel is under test.</summary>
    public bool IsOverridden(Guid fixtureId, string channelKey) => _overrides.ContainsKey((fixtureId, channelKey));

    /// <summary>The test value of a fixture channel, when it is under test.</summary>
    public bool TryGetValue(Guid fixtureId, string channelKey, out byte value)
    {
        if (_overrides.TryGetValue((fixtureId, channelKey), out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>True when any channel under test sits at this universe/address (DMX monitor).</summary>
    public bool IsAddressOverridden(int universe, int address)
    {
        foreach (var entry in _overrides.Values)
        {
            if (entry.Universe == universe && entry.Address == address)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the channel is under test but another tracked channel at the same address was touched later, so that
    /// one's value is what the engine sends (overlapping fixtures).
    /// </summary>
    public bool IsShadowed(Guid fixtureId, string channelKey)
    {
        if (!_overrides.TryGetValue((fixtureId, channelKey), out var own))
        {
            return false;
        }

        foreach (var entry in _overrides.Values)
        {
            if (entry.Universe == own.Universe && entry.Address == own.Address && entry.Sequence > own.Sequence)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Forgets channels an engine-wide clear removed (see <see cref="DmxEngine.ReleaseGeneration"/>), without sending
    /// anything. Cheap when nothing happened (one counter read). Returns the number of channels forgotten.
    /// </summary>
    public int Sync()
    {
        var generation = _engine.ReleaseGeneration;
        if (generation == _seenGeneration)
        {
            return 0;
        }

        _seenGeneration = generation;
        List<(Guid, string)>? gone = null;
        foreach (var (key, entry) in _overrides)
        {
            if (entry.Generation < generation)
            {
                (gone ??= []).Add(key);
            }
        }

        if (gone is null)
        {
            return 0;
        }

        foreach (var key in gone)
        {
            _overrides.Remove(key);
        }

        return gone.Count;
    }

    /// <summary>
    /// Call right after <see cref="DmxEngine.RefreshPatch"/> (same thread, so the commands queue after the refresh):
    /// re-resolves every channel under test. A channel whose fixture moved is cleared at its old address and set at the
    /// new one; a channel that no longer resolves (fixture deleted or disabled, mode changed, past 512, universe gone) is
    /// dropped and its old address cleared. Values are re-sent in touch order, so when two channels now share an address
    /// the last-touched one wins. Channels removed by an engine-wide clear are not re-sent. Returns the number of
    /// channels dropped because they no longer resolve.
    /// </summary>
    public int Reapply()
    {
        Sync();
        if (_overrides.Count == 0)
        {
            return 0;
        }

        var oldAddresses = new HashSet<(int, int)>();
        var updated = new List<TestOverride>(_overrides.Count);
        var dropped = 0;
        var generation = _engine.ReleaseGeneration;
        foreach (var entry in _overrides.Values)
        {
            oldAddresses.Add((entry.Universe, entry.Address));
            if (_engine.TryResolveChannel(entry.FixtureId, entry.ChannelKey, out var channel))
            {
                updated.Add(entry with { Universe = channel.Universe, Address = channel.Address, Generation = generation });
            }
            else
            {
                dropped++;
            }
        }

        updated.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        _overrides.Clear();
        var newAddresses = new HashSet<(int, int)>();
        foreach (var entry in updated)
        {
            _overrides[(entry.FixtureId, entry.ChannelKey)] = entry;
            newAddresses.Add((entry.Universe, entry.Address));
        }

        foreach (var (universe, address) in oldAddresses)
        {
            if (!newAddresses.Contains((universe, address)))
            {
                _engine.ClearChannelOverride(universe, address);
            }
        }

        foreach (var entry in updated)
        {
            _engine.SetChannelOverride(entry.Universe, entry.Address, entry.Value);
        }

        return dropped;
    }

    /// <summary>
    /// Forgets every channel without sending anything. Prefer <see cref="ReleaseAll"/> (which also clears the engine);
    /// this is for when the engine is known to be cleared already. Returns the number of channels forgotten.
    /// </summary>
    public int Reset()
    {
        var count = _overrides.Count;
        _overrides.Clear();
        return count;
    }

    /// <summary>Rounds half away from zero and clamps to 0–255; NaN becomes 0 (safety rule 9).</summary>
    public static byte ToByte(double value)
    {
        if (!(value > 0))
        {
            return 0;
        }

        return value >= 255 ? (byte)255 : (byte)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>After removing a key: re-send the latest other tracked value at the same address, else clear it.</summary>
    private void ReleaseAddress(int universe, int address)
    {
        TestOverride? latest = null;
        foreach (var entry in _overrides.Values)
        {
            if (entry.Universe == universe && entry.Address == address && (latest is null || entry.Sequence > latest.Value.Sequence))
            {
                latest = entry;
            }
        }

        if (latest is { } winner)
        {
            _engine.SetChannelOverride(universe, address, winner.Value);
        }
        else
        {
            _engine.ClearChannelOverride(universe, address);
        }
    }
}
