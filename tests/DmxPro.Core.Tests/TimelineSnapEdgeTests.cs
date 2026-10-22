using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

/// <summary>Beat grid and snapping edge cases: floating-point noise on beat lines, damaged tempos, huge ranges.</summary>
public class TimelineSnapEdgeTests
{
    public static TheoryData<double, double> TempoAndOffset => new()
    {
        { 100, 0 },
        { 123.45, 0 },
        { 97.3, 0.37 },
        { 140.62, 1.234 },
        { 128, 0.37 },
    };

    [Theory]
    [MemberData(nameof(TempoAndOffset))]
    public void BeatIndexAt_ExactlyOnBeatLine_ReturnsThatBeat(double bpm, double offset)
    {
        var wrong = new List<long>();
        for (long k = -3; k < 5000; k++)
        {
            var t = TimelineSnap.BeatTime(k, bpm, offset);
            if (t < 0)
            {
                continue;
            }

            if (TimelineSnap.BeatIndexAt(t, bpm, offset) != k)
            {
                wrong.Add(k);
            }
        }

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData(TimelineSnapMode.Beat, 5e-324)]
    [InlineData(TimelineSnapMode.Bar, 1e-307)]
    [InlineData(TimelineSnapMode.Bar, 1e-306)]
    public void Snap_TinyBpmFromDamagedFile_ReturnsFiniteTime(TimelineSnapMode mode, double bpm)
    {
        var snapped = TimelineSnap.Snap(10, mode, bpm, 0);

        Assert.True(double.IsFinite(snapped), $"Snap returned {snapped}");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(123.45)]
    [InlineData(97.3)]
    [InlineData(140.62)]
    [InlineData(128)]
    public void BeatLines_OffsetOnWholeBeat_FirstLineIsAtShowTimeZero(double bpm)
    {
        var beat = TimelineSnap.BeatSeconds(bpm);
        var missing = new List<int>();
        for (var k = 1; k < 4; k++)
        {
            // The first beat of the music is k beats in: show time 0 is then a beat line (a pickup beat).
            var offset = k * beat;
            var first = double.NaN;
            foreach (var line in TimelineSnap.BeatLines(0, 10, bpm, offset))
            {
                first = line.Seconds;
                break;
            }

            // Exactly a line at 0, never a time before the show start (rounding could give -1e-17).
            if (!(first >= 0 && first < 1e-9) || TimelineSnap.FirstBeatAtOrAfter(0, bpm, offset) is not (>= 0 and < 1e-9))
            {
                missing.Add(k);
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void BeatLines_HugeFrom_LinesAreNotBeforeFrom()
    {
        const double from = 1e20;
        foreach (var line in TimelineSnap.BeatLines(from, from + 10, 120, 0))
        {
            Assert.True(line.Seconds >= from - 1, $"line at {line.Seconds} is before {from}");
            break;
        }
    }
}
