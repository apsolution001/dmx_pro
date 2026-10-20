using DmxPro.Output.Protocols;

namespace DmxPro.Output.Tests;

public class UniverseMappingTests
{
    [Theory]
    [InlineData(1, 0, 0, 0, 0x0000)]
    [InlineData(2, 0, 0, 0, 0x0001)]
    [InlineData(16, 0, 0, 0, 0x000F)]
    [InlineData(17, 0, 0, 0, 0x0010)] // rolls into Sub-Net 1
    [InlineData(1, 1, 2, 3, 0x0123)]
    [InlineData(3, 1, 2, 3, 0x0125)]
    [InlineData(1, 127, 15, 15, 0x7FFF)]
    public void TryGetArtNetPortAddress_ValidInput_MapsFromZero(int universe, int net, int subNet, int artUniverse, int expected)
    {
        Assert.True(UniverseMapping.TryGetArtNetPortAddress(universe, net, subNet, artUniverse, out var portAddress));
        Assert.Equal(expected, portAddress);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(2, 127, 15, 15)]
    [InlineData(1, 128, 0, 0)]
    [InlineData(1, 0, 16, 0)]
    [InlineData(1, 0, 0, 16)]
    [InlineData(32769, 0, 0, 0)]
    public void TryGetArtNetPortAddress_OutOfRange_ReturnsFalse(int universe, int net, int subNet, int artUniverse)
    {
        Assert.False(UniverseMapping.TryGetArtNetPortAddress(universe, net, subNet, artUniverse, out _));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(63999, 0, 63999)]
    [InlineData(10, 100, 110)]
    [InlineData(5, -4, 1)]
    public void TryGetSacnUniverse_ValidInput_Maps(int universe, int offset, int expected)
    {
        Assert.True(UniverseMapping.TryGetSacnUniverse(universe, offset, out var sacnUniverse));
        Assert.Equal(expected, sacnUniverse);
    }

    [Theory]
    [InlineData(0, 0)]      // project universe 0 does not exist
    [InlineData(1, -1)]     // sACN universe 0 is reserved
    [InlineData(64000, 0)]  // above 63999
    [InlineData(63999, 1)]
    public void TryGetSacnUniverse_OutOfRange_ReturnsFalse(int universe, int offset)
    {
        Assert.False(UniverseMapping.TryGetSacnUniverse(universe, offset, out _));
    }
}
