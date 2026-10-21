using DmxPro.Core.Profiles;

namespace DmxPro.Core.Patching;

/// <summary>
/// Preview of patching <see cref="Quantity"/> fixtures of one mode in a row (as the Add Fixture dialog does): the
/// footprint of each fixture, whether the block fits, and which existing fixtures it would overlap.
/// Fixture <c>i</c> starts at <c>start + i × (channels + gap)</c>, the same layout as <c>Project.AddFixtures</c>.
/// </summary>
public sealed class PatchPlan
{
    /// <summary>Most fixtures the dialog patches at once (one per address of a universe).</summary>
    public const int MaxQuantity = DmxAddress.UniverseSize;

    /// <summary>Lists with more ranges than this are shortened to "first … last".</summary>
    public const int MaxRangesShown = 3;

    private PatchPlan(int universe, int startAddress, int channelCount, int quantity, int gap)
    {
        Universe = universe;
        StartAddress = startAddress;
        ChannelCount = channelCount;
        Quantity = quantity;
        Gap = gap;
    }

    public int Universe { get; }

    public int StartAddress { get; }

    /// <summary>Channels of one fixture (the selected mode).</summary>
    public int ChannelCount { get; }

    public int Quantity { get; }

    /// <summary>Unused channels between consecutive fixtures.</summary>
    public int Gap { get; }

    /// <summary>One footprint per planned fixture; empty when the inputs themselves are invalid.</summary>
    public IReadOnlyList<FixtureFootprint> Footprints { get; private set; } = [];

    /// <summary>Existing enabled fixtures in the same universe that share at least one address with the plan.</summary>
    public IReadOnlyList<Fixture> Conflicts { get; private set; } = [];

    /// <summary>Why the plan cannot be applied, or null when it is valid.</summary>
    public string? Problem { get; private set; }

    public bool IsValid => Problem is null;

    /// <summary>Channels the whole block spans, gaps between fixtures included.</summary>
    public int TotalChannels => BlockChannels(ChannelCount, Quantity, Gap);

    /// <summary>Last address used by the last fixture.</summary>
    public int EndAddress => StartAddress + TotalChannels - 1;

    /// <summary>Channels spanned by <paramref name="quantity"/> fixtures with <paramref name="gap"/> between them.</summary>
    public static int BlockChannels(int channelCount, int quantity, int gap) =>
        (Math.Max(channelCount, 0) * Math.Max(quantity, 0)) + (Math.Max(gap, 0) * Math.Max(0, quantity - 1));

    /// <summary>Builds and validates a plan against the fixtures already patched.</summary>
    public static PatchPlan Create(
        int universe,
        int startAddress,
        int channelCount,
        int quantity,
        int gap,
        IReadOnlyList<Fixture> existing,
        IProfileResolver profiles)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(profiles);

        return Create(
            universe,
            startAddress,
            channelCount,
            quantity,
            gap,
            () => UniverseOccupancy.Build(existing, profiles, universe));
    }

    /// <summary>
    /// Builds and validates a plan against a prebuilt <paramref name="occupancy"/> snapshot of the plan's universe, so
    /// a dialog that re-plans on every keystroke doesn't rescan every patched fixture. Same result as the overload that
    /// takes the fixture list.
    /// </summary>
    public static PatchPlan Create(
        int universe,
        int startAddress,
        int channelCount,
        int quantity,
        int gap,
        UniverseOccupancy occupancy)
    {
        ArgumentNullException.ThrowIfNull(occupancy);
        if (occupancy.Universe != universe)
        {
            throw new ArgumentException($"Occupancy is for universe {occupancy.Universe}, not {universe}.", nameof(occupancy));
        }

        return Create(universe, startAddress, channelCount, quantity, gap, () => occupancy);
    }

    private static PatchPlan Create(
        int universe,
        int startAddress,
        int channelCount,
        int quantity,
        int gap,
        Func<UniverseOccupancy> occupancyOf)
    {
        var plan = new PatchPlan(universe, startAddress, channelCount, quantity, gap);
        plan.Problem = plan.CheckInputs();
        if (plan.Problem is not null)
        {
            return plan;
        }

        var footprints = new List<FixtureFootprint>(quantity);
        for (var i = 0; i < quantity; i++)
        {
            footprints.Add(new FixtureFootprint(universe, startAddress + (i * (channelCount + gap)), channelCount));
        }

        plan.Footprints = footprints;

        var occupancy = occupancyOf();
        var conflicts = occupancy.FindConflicts(footprints);
        plan.Conflicts = conflicts;
        if (conflicts.Count > 0)
        {
            var first = conflicts[0];
            var more = conflicts.Count > 1 ? $" and {conflicts.Count - 1} more" : string.Empty;
            plan.Problem = $"Overlaps \"{first.Name}\" ({occupancy.FootprintOf(first)}){more}. " +
                           "Pick a free address or use \"Use next free address\".";
        }

        return plan;
    }

    /// <summary>
    /// Address ranges of the planned fixtures, e.g. "1–8, 9–16, 17–24", or "1–8 … 89–96" when there are more than
    /// <see cref="MaxRangesShown"/>. One-channel fixtures show a single address ("5").
    /// </summary>
    public string DescribeRanges()
    {
        if (Footprints.Count == 0)
        {
            return string.Empty;
        }

        if (Footprints.Count > MaxRangesShown)
        {
            return $"{Range(Footprints[0])} … {Range(Footprints[^1])}";
        }

        return string.Join(", ", Footprints.Select(Range));
    }

    /// <summary>Preview sentence: "Will use Universe 1, addresses 1–8, 9–16." (empty when invalid inputs).</summary>
    public string Describe()
    {
        if (Footprints.Count == 0)
        {
            return string.Empty;
        }

        var noun = Footprints.Count == 1 && ChannelCount == 1 ? "address" : "addresses";
        return $"Will use Universe {Universe}, {noun} {DescribeRanges()}.";
    }

    private static string Range(FixtureFootprint footprint) =>
        footprint.ChannelCount == 1 ? $"{footprint.StartAddress}" : $"{footprint.StartAddress}–{footprint.EndAddress}";

    private string? CheckInputs()
    {
        if (ChannelCount < 1)
        {
            return "Pick a fixture profile and mode.";
        }

        if (!DmxAddress.IsValidUniverse(Universe))
        {
            return $"Universe must be 1–{DmxAddress.MaxUniverse}.";
        }

        if (!DmxAddress.IsValid(StartAddress))
        {
            return $"Address must be {DmxAddress.Min}–{DmxAddress.Max}.";
        }

        if (Quantity is < 1 or > MaxQuantity)
        {
            return $"Quantity must be 1–{MaxQuantity}.";
        }

        if (Gap < 0)
        {
            return "Address gap cannot be negative.";
        }

        if (TotalChannels > DmxAddress.UniverseSize)
        {
            return $"Needs {TotalChannels} channels, more than one universe ({DmxAddress.UniverseSize}). " +
                   "Add fewer fixtures or reduce the gap.";
        }

        if (EndAddress > DmxAddress.Max)
        {
            return $"Runs past address {DmxAddress.Max} (would end at {EndAddress}). " +
                   $"Start at {DmxAddress.Max - TotalChannels + 1} or lower.";
        }

        return null;
    }
}
