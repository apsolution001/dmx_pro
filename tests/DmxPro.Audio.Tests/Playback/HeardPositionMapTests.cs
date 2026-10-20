using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

public class HeardPositionMapTests
{
    [Fact]
    public void TryMap_InsideAudioSpan_ReturnsSourceFramePlusOffset()
    {
        var map = new HeardPositionMap();
        map.Record(1, OutputSpanKind.Audio, 1000, 500);

        Assert.True(map.TryMap(123, out HeardFrame heard));

        Assert.Equal(new HeardFrame(1123, 1, OutputSpanKind.Audio), heard);
    }

    [Fact]
    public void TryMap_AcrossSeek_ReturnsFrameOfTheSpanBeingHeard()
    {
        var map = new HeardPositionMap();
        map.Record(1, OutputSpanKind.Audio, 0, 1000); // output 0..999
        map.Record(2, OutputSpanKind.Audio, 50_000, 1000); // seek: output 1000..1999

        Assert.True(map.TryMap(999, out HeardFrame before));
        Assert.True(map.TryMap(1000, out HeardFrame after));
        Assert.True(map.TryMap(1500, out HeardFrame later));

        Assert.Equal(new HeardFrame(999, 1, OutputSpanKind.Audio), before);
        Assert.Equal(new HeardFrame(50_000, 2, OutputSpanKind.Audio), after);
        Assert.Equal(50_500, later.SourceFrame);
    }

    [Fact]
    public void TryMap_HaltedSpan_HoldsSourceFrame()
    {
        var map = new HeardPositionMap();
        map.Record(1, OutputSpanKind.Audio, 0, 100);
        map.Record(1, OutputSpanKind.Halted, 100, 1000);

        Assert.True(map.TryMap(800, out HeardFrame heard));

        Assert.Equal(new HeardFrame(100, 1, OutputSpanKind.Halted), heard);
    }

    [Fact]
    public void TryMap_PastNewestSpan_HoldsAtItsEnd()
    {
        var map = new HeardPositionMap();
        map.Record(3, OutputSpanKind.Audio, 2000, 100);

        Assert.True(map.TryMap(10_000, out HeardFrame heard));

        Assert.Equal(2100, heard.SourceFrame);
    }

    [Fact]
    public void Record_ContiguousReads_MergeSoRingNeverOverflows()
    {
        var map = new HeardPositionMap(capacity: 4);
        for (int i = 0; i < 10_000; i++)
        {
            map.Record(1, OutputSpanKind.Audio, i * 10L, 10);
        }

        Assert.True(map.TryMap(0, out HeardFrame first));
        Assert.Equal(0, first.SourceFrame);
        Assert.Equal(100_000, map.OutputFrames);
    }

    [Fact]
    public void TryMap_OlderThanRing_ReturnsFalse()
    {
        var map = new HeardPositionMap(capacity: 2);
        map.Record(1, OutputSpanKind.Audio, 0, 10);
        map.Record(2, OutputSpanKind.Audio, 500, 10);
        map.Record(3, OutputSpanKind.Audio, 900, 10);

        Assert.False(map.TryMap(5, out _));
        Assert.True(map.TryMap(15, out HeardFrame heard));
        Assert.Equal(505, heard.SourceFrame);
    }

    [Fact]
    public void SilentSinceOutputFrame_TracksTrailingSilence()
    {
        var map = new HeardPositionMap();
        Assert.Equal(-1, map.SilentSinceOutputFrame);

        map.Record(1, OutputSpanKind.Audio, 0, 100);
        Assert.Equal(-1, map.SilentSinceOutputFrame);

        map.Record(1, OutputSpanKind.Halted, 100, 50);
        map.Record(1, OutputSpanKind.Halted, 100, 50);
        Assert.Equal(100, map.SilentSinceOutputFrame);

        map.Record(2, OutputSpanKind.Audio, 0, 10);
        Assert.Equal(-1, map.SilentSinceOutputFrame);
    }

    [Fact]
    public void Reset_StartsCountingFromZero()
    {
        var map = new HeardPositionMap();
        map.Record(1, OutputSpanKind.Audio, 0, 100);

        map.Reset();
        map.Record(2, OutputSpanKind.Audio, 7000, 100);

        Assert.True(map.TryMap(0, out HeardFrame heard));
        Assert.Equal(new HeardFrame(7000, 2, OutputSpanKind.Audio), heard);
    }
}
