using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class ShowNormalizeTests
{
    [Theory]
    [InlineData(double.NaN, Show.DefaultBpm)]
    [InlineData(double.PositiveInfinity, Show.DefaultBpm)]
    [InlineData(0, Show.DefaultBpm)]
    [InlineData(-90, Show.DefaultBpm)]
    [InlineData(5e-324, Show.MinBpm)]
    [InlineData(5, Show.MinBpm)]
    [InlineData(128.5, 128.5)]
    [InlineData(1e9, Show.MaxBpm)]
    public void Normalize_Bpm_FallsBackOrClampsToEditorRange(double bpm, double expected)
    {
        var show = new Show { Bpm = bpm };

        show.Normalize();

        Assert.Equal(expected, show.Bpm);
    }

    [Theory]
    [InlineData(double.NaN, Show.DefaultLengthSeconds)]
    [InlineData(double.NegativeInfinity, Show.DefaultLengthSeconds)]
    [InlineData(0, Show.DefaultLengthSeconds)]
    [InlineData(-3, Show.DefaultLengthSeconds)]
    [InlineData(42.5, 42.5)]
    [InlineData(1e12, Show.MaxLengthSeconds)]
    public void Normalize_Length_FallsBackOrCutsToMaximum(double length, double expected)
    {
        var show = new Show { LengthSeconds = length };

        show.Normalize();

        Assert.Equal(expected, show.LengthSeconds);
    }

    [Fact]
    public void NormalizeAudio_DamagedTempoAndLength_AlsoNormalizesTiming()
    {
        var show = new Show { Bpm = double.NaN, LengthSeconds = -1 };

        show.NormalizeAudio();

        Assert.Equal(Show.DefaultBpm, show.Bpm);
        Assert.Equal(Show.DefaultLengthSeconds, show.LengthSeconds);
    }
}
