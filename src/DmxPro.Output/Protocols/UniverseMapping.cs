namespace DmxPro.Output.Protocols;

/// <summary>
/// The only place that converts project universes (UI numbering, from 1) to protocol numbering (coding standards:
/// protocol-specific numbering lives in the Output layer).
/// </summary>
public static class UniverseMapping
{
    public const int SacnMinUniverse = 1;
    public const int SacnMaxUniverse = 63999;

    /// <summary>
    /// Art-Net Port-Address for <paramref name="projectUniverse"/>: the configured first Port-Address
    /// (<c>net·256 + subNet·16 + universe</c>) plus <c>projectUniverse − 1</c>.
    /// </summary>
    /// <returns><c>false</c> if any input is out of range or the result exceeds 32767.</returns>
    public static bool TryGetArtNetPortAddress(
        int projectUniverse,
        int net,
        int subNet,
        int universe,
        out ushort portAddress)
    {
        portAddress = 0;
        if (projectUniverse < 1
            || net is < 0 or > ArtNetPacket.MaxNet
            || subNet is < 0 or > 15
            || universe is < 0 or > 15)
        {
            return false;
        }

        var value = ((net << 8) | (subNet << 4) | universe) + (projectUniverse - 1);
        if (value > ArtNetPacket.MaxPortAddress)
        {
            return false;
        }

        portAddress = (ushort)value;
        return true;
    }

    /// <summary>sACN universe for <paramref name="projectUniverse"/>: <c>projectUniverse + offset</c>, valid 1–63999.</summary>
    public static bool TryGetSacnUniverse(int projectUniverse, int offset, out ushort sacnUniverse)
    {
        sacnUniverse = 0;
        if (projectUniverse < 1)
        {
            return false;
        }

        var value = (long)projectUniverse + offset;
        if (value is < SacnMinUniverse or > SacnMaxUniverse)
        {
            return false;
        }

        sacnUniverse = (ushort)value;
        return true;
    }
}
