using DmxPro.Core.Patching;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// Double-buffered hand-off of engine state to the UI. The engine fills the back frame without any lock, then swaps
/// back/front under a short lock. Readers copy the front frame into a new <see cref="EngineSnapshot"/> under the same
/// lock (allocation happens on the reader's thread, never in the tick).
/// </summary>
internal sealed class SnapshotStore
{
    private readonly object _gate = new();
    private Frame _back;
    private Frame _front;
    private bool _hasFrame;

    public SnapshotStore(int maxFunctions, int maxShows)
    {
        _back = new Frame(maxFunctions, maxShows);
        _front = new Frame(maxFunctions, maxShows);
    }

    /// <summary>The frame the engine fills before <see cref="Publish"/> (engine thread only).</summary>
    public Frame Back => _back;

    /// <summary>Makes <see cref="Back"/> the latest frame.</summary>
    public void Publish()
    {
        lock (_gate)
        {
            (_back, _front) = (_front, _back);
            _hasFrame = true;
        }
    }

    /// <summary>Looks up one show in the latest frame without copying anything.</summary>
    public bool TryGetShow(Guid showId, out ShowPlaybackInfo info, out long sequence)
    {
        lock (_gate)
        {
            info = default;
            if (!_hasFrame)
            {
                sequence = -1;
                return false;
            }

            sequence = _front.Sequence;
            for (var i = 0; i < _front.ShowCount; i++)
            {
                if (_front.Shows[i].ShowId == showId)
                {
                    info = _front.Shows[i];
                    return true;
                }
            }

            return false;
        }
    }

    public EngineSnapshot? Read()
    {
        lock (_gate)
        {
            return _hasFrame ? _front.ToSnapshot() : null;
        }
    }

    /// <summary>Pre-allocated snapshot storage.</summary>
    internal sealed class Frame
    {
        public Frame(int maxFunctions, int maxShows)
        {
            Functions = new ActiveFunctionInfo[maxFunctions];
            Shows = new ShowPlaybackInfo[maxShows];
        }

        public long Sequence { get; set; }

        public double TimeSeconds { get; set; }

        public bool Blackout { get; set; }

        public double GrandMaster { get; set; }

        public StrobeLimit StrobeLimit { get; set; }

        public int[] Universes { get; private set; } = [];

        public byte[][] Data { get; private set; } = [];

        public ActiveFunctionInfo[] Functions { get; }

        public int FunctionCount { get; set; }

        public ShowPlaybackInfo[] Shows { get; }

        public int ShowCount { get; set; }

        /// <summary>Requested (pre-master) values per universe slot, same layout as <see cref="Data"/>.</summary>
        public byte[][] Requested { get; private set; } = [];

        /// <summary>
        /// Copies the merged and requested universes. Allocates only when the universe set changed (project load).
        /// </summary>
        public void CopyUniverses(int[] universes, byte[][] output, byte[][] requested)
        {
            if (!ReferenceEquals(Universes, universes))
            {
                Universes = universes;
                Data = new byte[universes.Length][];
                Requested = new byte[universes.Length][];
                for (var i = 0; i < Data.Length; i++)
                {
                    Data[i] = new byte[DmxAddress.UniverseSize];
                    Requested[i] = new byte[DmxAddress.UniverseSize];
                }
            }

            for (var i = 0; i < output.Length; i++)
            {
                output[i].AsSpan().CopyTo(Data[i]);
                requested[i].AsSpan().CopyTo(Requested[i]);
            }
        }

        public EngineSnapshot ToSnapshot()
        {
            var data = new byte[Data.Length][];
            var requested = new byte[Requested.Length][];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte[])Data[i].Clone();
                requested[i] = (byte[])Requested[i].Clone();
            }

            return new EngineSnapshot(
                Sequence,
                TimeSpan.FromSeconds(TimeSeconds),
                Blackout,
                GrandMaster,
                StrobeLimit,
                (int[])Universes.Clone(),
                data,
                requested,
                Functions.AsSpan(0, FunctionCount).ToArray(),
                Shows.AsSpan(0, ShowCount).ToArray());
        }
    }
}
