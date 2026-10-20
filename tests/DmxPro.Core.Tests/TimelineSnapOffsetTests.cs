using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class TimelineSnapOffsetTests
{
    [Theory]
    [InlineData(TimelineSnapMode.Off, 1.23456)]
    [InlineData(TimelineSnapMode.Tenth, 1.26)]
    [InlineData(TimelineSnapMode.Beat, 1.3)]
    [InlineData(TimelineSnapMode.Bar, 3.1)]
    [InlineData(TimelineSnapMode.Beat, 0.2)]
    public void Snap_ZeroOffset_MatchesOldOverload(TimelineSnapMode mode, double input)
    {
        Assert.Equal(TimelineSnap.Snap(input, mode, 120), TimelineSnap.Snap(input, mode, 120, 0));
    }

    [Theory]
    [InlineData(1.2, 1.3)]
    [InlineData(1.5, 1.3)]
    [InlineData(1.6, 1.8)]
    [InlineData(10.05, 10.3)]
    [InlineData(0.3, 0.3)]
    [InlineData(0.1, 0)]
    [InlineData(0.2, 0.3)]
    public void Snap_BeatWithOffset_SnapsToShiftedGrid(double input, double expected)
    {
        // 120 BPM, first beat at 2.3 s: beat lines at 0.3, 0.8, 1.3, 1.8 ... (extended backwards to time 0).
        Assert.Equal(expected, TimelineSnap.Snap(input, TimelineSnapMode.Beat, 120, 2.3), 6);
    }

    [Theory]
    [InlineData(2.0, 2.3)]
    [InlineData(4.4, 4.3)]
    [InlineData(3.2, 2.3)]
    [InlineData(3.4, 4.3)]
    [InlineData(0.2, 0.3)]
    public void Snap_BarWithOffset_SnapsToShiftedBars(double input, double expected)
    {
        // Bars of 2 s from 2.3 s: 0.3, 2.3, 4.3 ...
        Assert.Equal(expected, TimelineSnap.Snap(input, TimelineSnapMode.Bar, 120, 2.3), 6);
    }

    [Theory]
    [InlineData(-0.2)]
    [InlineData(-100.2)]
    [InlineData(1e9 + 0.3)]
    public void Snap_OffsetEquivalentModuloBeat_GivesSameResult(double offset)
    {
        Assert.Equal(1.3, TimelineSnap.Snap(1.2, TimelineSnapMode.Beat, 120, offset), 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Snap_NonFiniteOffset_CountsAsZero(double offset)
    {
        Assert.Equal(1.0, TimelineSnap.Snap(1.2, TimelineSnapMode.Beat, 120, offset));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Snap_BadInputWithOffset_ReturnsZero(double input)
    {
        Assert.Equal(0, TimelineSnap.Snap(input, TimelineSnapMode.Beat, 120, 0.3));
    }

    [Theory]
    [InlineData(TimelineSnapMode.Tenth, 1.26, 1.3)]
    [InlineData(TimelineSnapMode.Off, 1.23456, 1.235)]
    public void Snap_TenthAndOff_IgnoreOffset(TimelineSnapMode mode, double input, double expected)
    {
        Assert.Equal(expected, TimelineSnap.Snap(input, mode, 120, 0.04), 6);
    }

    [Fact]
    public void Snap_BadBpmWithOffset_UsesDefaultBpm()
    {
        Assert.Equal(1.3, TimelineSnap.Snap(1.2, TimelineSnapMode.Beat, double.NaN, 0.3), 6);
    }

    [Theory]
    [InlineData(0, 0.3)]
    [InlineData(0.3, 0.3)]
    [InlineData(0.31, 0.8)]
    [InlineData(-5, 0.3)]
    [InlineData(double.NaN, 0.3)]
    [InlineData(100.29999999999, 100.3)]
    public void FirstBeatAtOrAfter_WithOffset_FindsNextLine(double time, double expected)
    {
        Assert.Equal(expected, TimelineSnap.FirstBeatAtOrAfter(time, 120, 2.3), 6);
    }

    [Fact]
    public void FirstBeatAtOrAfter_ZeroOffset_StartsAtZero()
    {
        Assert.Equal(0, TimelineSnap.FirstBeatAtOrAfter(0, 120, 0));
    }

    [Theory]
    [InlineData(2.3, 0.3)]
    [InlineData(-0.2, 1.8)]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5.5, 1.5)]
    [InlineData(double.NaN, 0)]
    public void GridOrigin_FoldsOffsetIntoFirstBar(double offset, double expected)
    {
        Assert.Equal(expected, TimelineSnap.GridOrigin(120, offset), 6);
    }

    [Theory]
    [InlineData(0.3, 0)]
    [InlineData(0.79, 0)]
    [InlineData(0.8, 1)]
    [InlineData(2.3, 4)]
    [InlineData(0.1, -1)]
    [InlineData(double.NaN, -1)]
    public void BeatIndexAt_WithOffset_CountsFromGridOrigin(double time, long expected)
    {
        Assert.Equal(expected, TimelineSnap.BeatIndexAt(time, 120, 2.3));
    }

    [Fact]
    public void BeatIndexAt_HugeTime_DoesNotOverflow()
    {
        var index = TimelineSnap.BeatIndexAt(1e300, 120, 0);

        Assert.True(index > 0);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(-4, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public void IsBarStart_EveryFourthBeat(long index, bool expected)
    {
        Assert.Equal(expected, TimelineSnap.IsBarStart(index));
    }

    [Fact]
    public void BeatTime_InverseOfBeatIndexAt()
    {
        for (long i = -1; i < 1000; i++)
        {
            var t = TimelineSnap.BeatTime(i, 128, 0.37);
            Assert.Equal(i, TimelineSnap.BeatIndexAt(t + 1e-6, 128, 0.37));
        }
    }

    [Fact]
    public void BeatLines_WithOffset_ExtendsBackToZeroAndMarksBars()
    {
        var lines = new List<BeatLine>();
        foreach (var line in TimelineSnap.BeatLines(0, 2.3, 120, 2.3))
        {
            lines.Add(line);
        }

        Assert.Equal([0.3, 0.8, 1.3, 1.8, 2.3], lines.Select(l => Math.Round(l.Seconds, 6)));
        Assert.Equal([0L, 1, 2, 3, 4], lines.Select(l => l.BeatIndex));
        Assert.Equal([true, false, false, false, true], lines.Select(l => l.IsBarStart));
    }

    [Fact]
    public void BeatLines_OffsetInsideFirstBar_HasPickupBeatsBeforeOrigin()
    {
        var lines = new List<BeatLine>();
        foreach (var line in TimelineSnap.BeatLines(0, 1.6, 120, 1.5))
        {
            lines.Add(line);
        }

        // Origin is 1.5 s (first bar line); beats at 0, 0.5, 1.0 come before it.
        Assert.Equal([0.0, 0.5, 1.0, 1.5], lines.Select(l => Math.Round(l.Seconds, 6)));
        Assert.Equal([-3L, -2, -1, 0], lines.Select(l => l.BeatIndex));
        Assert.Equal([false, false, false, true], lines.Select(l => l.IsBarStart));
    }

    [Fact]
    public void BeatLines_VisibleWindow_StartsAtFirstLineInWindow()
    {
        var lines = new List<double>();
        foreach (var line in TimelineSnap.BeatLines(60.1, 61.4, 120, 0.25))
        {
            lines.Add(Math.Round(line.Seconds, 6));
        }

        Assert.Equal([60.25, 60.75, 61.25], lines);
    }

    [Fact]
    public void BeatLines_BarStride_YieldsOnlyBarLines()
    {
        var lines = new List<BeatLine>();
        foreach (var line in TimelineSnap.BeatLines(0.5, 9, 120, 2.3, TimelineSnap.BeatsPerBar))
        {
            lines.Add(line);
        }

        Assert.Equal([2.3, 4.3, 6.3, 8.3], lines.Select(l => Math.Round(l.Seconds, 6)));
        Assert.All(lines, l => Assert.True(l.IsBarStart));
    }

    [Fact]
    public void BeatLines_BarStrideWithPickup_SkipsNegativeTimes()
    {
        var lines = new List<double>();
        foreach (var line in TimelineSnap.BeatLines(0, 4, 120, 1.5, TimelineSnap.BeatsPerBar))
        {
            lines.Add(Math.Round(line.Seconds, 6));
        }

        Assert.Equal([1.5, 3.5], lines);
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(double.NaN, 10)]
    [InlineData(0, double.NaN)]
    [InlineData(0, -1)]
    public void BeatLines_EmptyOrBadRange_YieldsNothing(double from, double to)
    {
        var count = 0;
        foreach (var unused in TimelineSnap.BeatLines(from, to, 120, 0))
        {
            count++;
        }

        if (double.IsNaN(from))
        {
            // A NaN start counts as 0: lines from 0 to 10 s.
            Assert.Equal(21, count);
        }
        else
        {
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public void BeatLines_DefaultRange_IsEmpty()
    {
        var count = 0;
        foreach (var unused in default(BeatLineRange))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Fact]
    public void BeatLines_HugeRangeOrTempo_IsCapped()
    {
        var count = 0;
        foreach (var unused in TimelineSnap.BeatLines(0, 1e12, 1e6, 0))
        {
            count++;
        }

        Assert.Equal(TimelineSnap.MaxBeatLines, count);
    }

    [Fact]
    public void BeatLines_LongShow_DoesNotDrift()
    {
        BeatLine last = default;
        foreach (var line in TimelineSnap.BeatLines(0, 3600, 128, 0.123))
        {
            last = line;
        }

        Assert.Equal(0.123 + (last.BeatIndex * 60.0 / 128), last.Seconds, 9);
        Assert.True(last.Seconds <= 3600);
        Assert.True(last.Seconds > 3600 - (60.0 / 128));
    }
}
