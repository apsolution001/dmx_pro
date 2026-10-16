namespace DmxPro.Core.Patching;

/// <summary>The DMX address range a fixture occupies in its universe.</summary>
public readonly record struct FixtureFootprint(int Universe, int StartAddress, int ChannelCount)
{
    public int EndAddress => DmxAddress.EndAddress(StartAddress, ChannelCount);

    public bool FitsInUniverse => DmxAddress.IsValid(StartAddress) && EndAddress <= DmxAddress.Max;

    public bool Overlaps(FixtureFootprint other) =>
        Universe == other.Universe && StartAddress <= other.EndAddress && other.StartAddress <= EndAddress;

    public override string ToString() => $"U{Universe} {StartAddress}–{EndAddress}";
}
