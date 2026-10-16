namespace DmxPro.Core.Patching;

/// <summary>
/// DMX addresses are 1-based (1–512) everywhere in the domain and UI. This is the only place that converts
/// them to 0-based buffer indexes.
/// </summary>
public static class DmxAddress
{
    public const int Min = 1;
    public const int Max = 512;
    public const int UniverseSize = 512;

    /// <summary>Highest universe number the project model accepts (UI numbering, starts at 1).</summary>
    public const int MaxUniverse = 63999;

    public static bool IsValid(int address) => address is >= Min and <= Max;

    public static bool IsValidUniverse(int universe) => universe is >= 1 and <= MaxUniverse;

    /// <summary>Converts a 1-based DMX address to a 0-based index into a 512-byte universe buffer.</summary>
    public static int ToIndex(int address)
    {
        if (!IsValid(address))
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "DMX address must be 1–512.");
        }

        return address - 1;
    }

    /// <summary>Last address occupied by a fixture of <paramref name="channelCount"/> channels at <paramref name="start"/>.</summary>
    public static int EndAddress(int start, int channelCount) => start + Math.Max(channelCount, 1) - 1;
}
