using DmxPro.Core.Patching;

namespace DmxPro.Engine.Runtime;

/// <summary>
/// Everything the tick needs for one loaded project, built off the engine thread by <see cref="EngineModelBuilder"/>:
/// the patch as flat per-channel tables (index = universe slot × 512 + address index), compiled functions and shows,
/// and the pre-allocated working buffers. The lookup parts are immutable after build; the buffers are touched only by
/// the engine thread.
/// </summary>
internal sealed class EngineModel
{
    /// <summary>Marker in <see cref="Overrides"/> for "no override".</summary>
    public const short NoOverride = -1;

    public EngineModel(
        int[] universes,
        byte[] defaults,
        bool[] isIntensity,
        bool[] isStrobe,
        byte[] strobeSafe,
        Dictionary<(Guid FixtureId, string ChannelKey), PatchedChannel> patch,
        Dictionary<Guid, FunctionProgram> functions,
        Dictionary<Guid, ShowProgram> shows,
        PatchIndex? patchIndex = null)
    {
        PatchIndex = patchIndex ?? new PatchIndex();
        Universes = universes;
        Defaults = defaults;
        IsIntensity = isIntensity;
        IsStrobe = isStrobe;
        StrobeSafe = strobeSafe;
        Patch = patch;
        Functions = functions;
        Shows = shows;

        Layers = new LayerBuffer[3];
        for (var i = 0; i < Layers.Length; i++)
        {
            Layers[i] = new LayerBuffer(isIntensity);
        }

        Overrides = new short[defaults.Length];
        Array.Fill(Overrides, NoOverride);

        Output = new byte[universes.Length][];
        Requested = new byte[universes.Length][];
        for (var i = 0; i < Output.Length; i++)
        {
            Output[i] = new byte[DmxAddress.UniverseSize];
            Requested[i] = new byte[DmxAddress.UniverseSize];
        }
    }

    /// <summary>Project universe numbers (from 1), ascending; the position is the universe slot.</summary>
    public int[] Universes { get; }

    public int ChannelCount => Defaults.Length;

    public byte[] Defaults { get; }

    public bool[] IsIntensity { get; }

    public bool[] IsStrobe { get; }

    /// <summary>Value forced on strobe channels while strobe is not allowed.</summary>
    public byte[] StrobeSafe { get; }

    public Dictionary<(Guid FixtureId, string ChannelKey), PatchedChannel> Patch { get; }

    public Dictionary<Guid, FunctionProgram> Functions { get; }

    /// <summary>
    /// Compile-time patch lookup this model was built with, so a preview draft compiled later resolves to exactly the
    /// same flat channels. Immutable after build; never used by the tick.
    /// </summary>
    public PatchIndex PatchIndex { get; }

    public Dictionary<Guid, ShowProgram> Shows { get; }

    /// <summary>One accumulator per <see cref="EngineLayer"/> (engine thread only).</summary>
    public LayerBuffer[] Layers { get; }

    /// <summary>Manual override per flat channel, or <see cref="NoOverride"/> (engine thread only).</summary>
    public short[] Overrides { get; }

    /// <summary>Merged output per universe slot (engine thread only).</summary>
    public byte[][] Output { get; }

    /// <summary>
    /// Merged value per universe slot before Grand Master, Blackout and strobe safety, i.e. what the functions and
    /// manual overrides ask for (engine thread only). Used by copy so a paste does not apply the masters twice.
    /// </summary>
    public byte[][] Requested { get; }

    /// <summary>Slot of a project universe, or -1 when the project does not use it. Linear scan: universes are few.</summary>
    public int SlotOf(int universe)
    {
        for (var i = 0; i < Universes.Length; i++)
        {
            if (Universes[i] == universe)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when <paramref name="other"/> has the same universes in the same order, so flat channel indexes compiled
    /// against one are valid in the other. Allocation-free (engine thread).
    /// </summary>
    public bool HasSameLayout(EngineModel other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Universes.Length != other.Universes.Length)
        {
            return false;
        }

        for (var i = 0; i < Universes.Length; i++)
        {
            if (Universes[i] != other.Universes[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Flat channel index of a universe/address, or -1 when the universe is not part of the project.</summary>
    public int FlatIndex(int universe, int address)
    {
        var slot = SlotOf(universe);
        return slot < 0 || !DmxAddress.IsValid(address) ? -1 : (slot * DmxAddress.UniverseSize) + DmxAddress.ToIndex(address);
    }
}
