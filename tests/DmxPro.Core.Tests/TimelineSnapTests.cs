using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class TimelineSnapTests
{
    [Theory]
    [InlineData(TimelineSnapMode.Off, 1.23456, 1.235)]
    [InlineData(TimelineSnapMode.Tenth, 1.26, 1.3)]
    [InlineData(TimelineSnapMode.Tenth, 0.3000001, 0.3)]
    [InlineData(TimelineSnapMode.Beat, 1.2, 1.0)]
    [InlineData(TimelineSnapMode.Beat, 1.3, 1.5)]
    [InlineData(TimelineSnapMode.Bar, 2.9, 2.0)]
    [InlineData(TimelineSnapMode.Bar, 3.1, 4.0)]
    public void Snap_At120Bpm_RoundsToNearestStep(TimelineSnapMode mode, double input, double expected)
    {
        Assert.Equal(expected, TimelineSnap.Snap(input, mode, 120), 6);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Snap_NegativeOrNotFinite_ReturnsZero(double input)
    {
        Assert.Equal(0, TimelineSnap.Snap(input, TimelineSnapMode.Beat, 120));
    }

    [Fact]
    public void StepSeconds_BeatAndBar_FollowBpm()
    {
        Assert.Equal(0.5, TimelineSnap.StepSeconds(TimelineSnapMode.Beat, 120), 9);
        Assert.Equal(2.0, TimelineSnap.StepSeconds(TimelineSnapMode.Bar, 120), 9);
        Assert.Equal(0.6, TimelineSnap.StepSeconds(TimelineSnapMode.Beat, 100), 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    [InlineData(double.NaN)]
    public void BeatSeconds_InvalidBpm_UsesDefaultBpm(double bpm)
    {
        Assert.Equal(60 / Show.DefaultBpm, TimelineSnap.BeatSeconds(bpm), 9);
    }
}
