using DmxPro.Core.Functions;

namespace DmxPro.Core.Tests;

public class SceneValueParsingTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("128", 128)]
    [InlineData("255", 255)]
    [InlineData(" 64 ", 64)]
    [InlineData("300", 255)]
    [InlineData("-5", 0)]
    [InlineData("12.6", 13)]
    [InlineData("1e9", 255)]
    public void TryParseDmxValue_Number_RoundsAndClamps(string text, int expected)
    {
        Assert.True(SceneEditing.TryParseDmxValue(text, out var value));
        Assert.Equal((byte)expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void TryParseDmxValue_Invalid_ReturnsFalse(string? text)
    {
        Assert.False(SceneEditing.TryParseDmxValue(text, out var value));
        Assert.Equal((byte)0, value);
    }
}
