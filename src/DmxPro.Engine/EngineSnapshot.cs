using DmxPro.Core.Patching;

namespace DmxPro.Engine;

/// <summary>
/// Immutable copy of the engine output and playback state for the UI (monitor, stage preview, "● Preview / ▶ Live"
/// markers, timeline cursor). Produced throttled; obtain the latest with <see cref="DmxEngine.GetSnapshot"/>.
/// </summary>
public sealed class EngineSnapshot
{
    private readonly int[] _universes;
    private readonly byte[][] _data;
    private readonly byte[][] _requested;

    internal EngineSnapshot(
        long sequence,
        TimeSpan engineTime,
        bool blackout,
        double grandMaster,
        StrobeLimit strobeLimit,
        int[] universes,
        byte[][] data,
        byte[][] requested,
        ActiveFunctionInfo[] activeFunctions,
        ShowPlaybackInfo[] shows)
    {
        Sequence = sequence;
        EngineTime = engineTime;
        Blackout = blackout;
        GrandMaster = grandMaster;
        StrobeLimit = strobeLimit;
        _universes = universes;
        _data = data;
        _requested = requested;
        ActiveFunctions = activeFunctions;
        Shows = shows;
    }

    /// <summary>Tick counter of the frame; increases with every tick.</summary>
    public long Sequence { get; }

    /// <summary>Total time the engine has advanced.</summary>
    public TimeSpan EngineTime { get; }

    /// <summary>Whether blackout was applied to this frame.</summary>
    public bool Blackout { get; }

    /// <summary>Grand Master applied to this frame, 0..1.</summary>
    public double GrandMaster { get; }

    /// <summary>
    /// Strobe limits active in this frame over all running functions and show items (safety rule 8): show it so the
    /// operator knows why a strobe is steady or slower than programmed.
    /// </summary>
    public StrobeLimit StrobeLimit { get; }

    /// <summary>Project universes (from 1) present in this snapshot.</summary>
    public IReadOnlyList<int> Universes => _universes;

    /// <summary>Functions running (or fading out) in any layer; show items are reported through <see cref="Shows"/>.</summary>
    public IReadOnlyList<ActiveFunctionInfo> ActiveFunctions { get; }

    /// <summary>Shows currently playing, with their positions.</summary>
    public IReadOnlyList<ShowPlaybackInfo> Shows { get; }

    /// <summary>
    /// True when at least one show is playing but muted because a console function is active
    /// (<see cref="ShowOverrideMode.MuteWholeShow"/>).
    /// </summary>
    public bool IsShowOverridden
    {
        get
        {
            foreach (var show in Shows)
            {
                if (show.IsMuted)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The 512 values of a universe, or an empty span when the project does not use it.</summary>
    public ReadOnlySpan<byte> GetUniverse(int universe)
    {
        var slot = Array.IndexOf(_universes, universe);
        return slot < 0 ? ReadOnlySpan<byte>.Empty : _data[slot];
    }

    /// <summary>Value at a 1-based address, or 0 when the universe is not part of the snapshot.</summary>
    public byte GetValue(int universe, int address)
    {
        var data = GetUniverse(universe);
        return data.IsEmpty ? (byte)0 : data[DmxAddress.ToIndex(address)];
    }

    /// <summary>
    /// The 512 values the running functions and manual overrides ask for, before Grand Master, Blackout and strobe
    /// safety (rounded + clamped like the output), or an empty span when the project does not use the universe.
    /// Use this, not <see cref="GetUniverse"/>, to copy a look: the output already has the masters applied.
    /// </summary>
    public ReadOnlySpan<byte> GetRequestedUniverse(int universe)
    {
        var slot = Array.IndexOf(_universes, universe);
        return slot < 0 ? ReadOnlySpan<byte>.Empty : _requested[slot];
    }

    /// <summary>
    /// Requested value (before Grand Master, Blackout and strobe safety) at a 1-based address, or 0 when the universe
    /// is not part of the snapshot.
    /// </summary>
    public byte GetRequestedValue(int universe, int address)
    {
        var data = GetRequestedUniverse(universe);
        return data.IsEmpty ? (byte)0 : data[DmxAddress.ToIndex(address)];
    }

    /// <summary>Playback position of a show, or null when it is not playing.</summary>
    public TimeSpan? GetShowPosition(Guid showId)
    {
        foreach (var show in Shows)
        {
            if (show.ShowId == showId)
            {
                return show.Position;
            }
        }

        return null;
    }
}
