using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class ShowAudioTimingTests
{
    [Fact]
    public void RequiredLength_NoAudio_ReturnsCurrentLength()
    {
        Assert.Equal(180, ShowAudioTiming.RequiredLength(new Show { LengthSeconds = 180 }));
    }

    [Fact]
    public void RequiredLength_SongUnknownLength_ReturnsCurrentLength()
    {
        var show = new Show { LengthSeconds = 60, Audio = new ShowAudio { OffsetSeconds = 5 } };

        Assert.Equal(60, ShowAudioTiming.RequiredLength(show));
    }

    [Theory]
    [InlineData(60, 0, 200, 200)]
    [InlineData(60, 10, 200, 210)]
    [InlineData(300, 10, 200, 300)]
    [InlineData(60, -20, 200, 180)]
    [InlineData(60, -500, 200, 60)]
    public void RequiredLength_WithSong_IsMaxOfLengthAndSongEnd(double length, double offset, double duration, double expected)
    {
        var show = new Show { LengthSeconds = length, Audio = new ShowAudio { OffsetSeconds = offset, DurationSeconds = duration } };

        Assert.Equal(expected, ShowAudioTiming.RequiredLength(show), 9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5.0)]
    [InlineData(double.PositiveInfinity)]
    public void RequiredLength_BadShowLength_UsesSongEnd(double length)
    {
        var show = new Show { LengthSeconds = length, Audio = new ShowAudio { DurationSeconds = 100 } };

        Assert.Equal(100, ShowAudioTiming.RequiredLength(show));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.PositiveInfinity)]
    public void SongEndSeconds_BadDuration_ReturnsNull(double duration)
    {
        Assert.Null(ShowAudioTiming.SongEndSeconds(new ShowAudio { DurationSeconds = duration }));
    }

    [Fact]
    public void SongEndSeconds_NonFiniteOffset_CountsAsZero()
    {
        Assert.Equal(90, ShowAudioTiming.SongEndSeconds(new ShowAudio { OffsetSeconds = double.NaN, DurationSeconds = 90 }));
    }

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 4, 6)]
    [InlineData(10, -4, 14)]
    [InlineData(10, double.NaN, 10)]
    public void ToSongTime_SubtractsOffset(double showTime, double offset, double expected)
    {
        Assert.Equal(expected, ShowAudioTiming.ToSongTime(showTime, offset));
        Assert.Equal(showTime, ShowAudioTiming.ToShowTime(expected, offset));
    }

    [Theory]
    [InlineData(0, false, -5)]
    [InlineData(4.999, false, -0.001)]
    [InlineData(5, true, 0)]
    [InlineData(50, true, 45)]
    [InlineData(104.999, true, 99.999)]
    [InlineData(105, false, 100)]
    [InlineData(500, false, 495)]
    public void TryGetSongTime_SongFrom5For100Seconds_SilentOutside(double showTime, bool playing, double songTime)
    {
        var audio = new ShowAudio { OffsetSeconds = 5, DurationSeconds = 100 };

        Assert.Equal(playing, ShowAudioTiming.TryGetSongTime(audio, showTime, out var actual));
        Assert.Equal(songTime, actual, 9);
    }

    [Fact]
    public void TryGetSongTime_NegativeOffset_StartsInsideSong()
    {
        var audio = new ShowAudio { OffsetSeconds = -30, DurationSeconds = 100 };

        Assert.True(ShowAudioTiming.TryGetSongTime(audio, 0, out var songTime));
        Assert.Equal(30, songTime);
    }

    [Fact]
    public void TryGetSongTime_UnknownDuration_PlaysAfterOffset()
    {
        var audio = new ShowAudio { OffsetSeconds = 2 };

        Assert.True(ShowAudioTiming.TryGetSongTime(audio, 10_000, out var songTime));
        Assert.Equal(9_998, songTime);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TryGetSongTime_NonFiniteShowTime_IsSilent(double showTime)
    {
        Assert.False(ShowAudioTiming.TryGetSongTime(new ShowAudio { DurationSeconds = 10 }, showTime, out var songTime));
        Assert.Equal(0, songTime);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(-1, 0)]
    [InlineData(7, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.NegativeInfinity, 0)]
    public void ClampVolume_OutOfRange_IsClamped(double volume, double expected)
    {
        Assert.Equal(expected, ShowAudioTiming.ClampVolume(volume));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(90, 1)]
    [InlineData(95, 0.5)]
    [InlineData(99, 0.1)]
    [InlineData(100, 0)]
    [InlineData(120, 0)]
    public void FadeOutGain_TenSecondFadeOfHundredSecondShow_RampsToZero(double time, double expected)
    {
        Assert.Equal(expected, ShowAudioTiming.FadeOutGain(time, 100, 10), 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    public void FadeOutGain_NoFade_IsOne(double fade)
    {
        Assert.Equal(1, ShowAudioTiming.FadeOutGain(99.9, 100, fade));
    }

    [Fact]
    public void FadeOutGain_FadeLongerThanShow_StartsBelowOne()
    {
        Assert.Equal(0.5, ShowAudioTiming.FadeOutGain(0, 10, 20), 9);
    }

    [Fact]
    public void EffectiveVolume_Muted_IsZero()
    {
        Assert.Equal(0, ShowAudioTiming.EffectiveVolume(new ShowAudio { Muted = true, Volume = 1 }, 0, 100));
    }

    [Fact]
    public void EffectiveVolume_DuringFade_MultipliesClampedVolume()
    {
        var audio = new ShowAudio { Volume = 3, FadeOutSeconds = 10 };

        Assert.Equal(0.5, ShowAudioTiming.EffectiveVolume(audio, 95, 100), 9);
        Assert.Equal(1, ShowAudioTiming.EffectiveVolume(audio, 10, 100), 9);
    }

    [Fact]
    public void Normalize_BadValues_AreCorrected()
    {
        var audio = new ShowAudio
        {
            FilePath = null!,
            RelativePath = " ",
            Volume = 4,
            FadeOutSeconds = -1,
            OffsetSeconds = double.NaN,
            DurationSeconds = -10,
            DetectedBpm = 0,
        };

        audio.Normalize();

        Assert.Equal(string.Empty, audio.FilePath);
        Assert.Null(audio.RelativePath);
        Assert.Equal(1, audio.Volume);
        Assert.Equal(0, audio.FadeOutSeconds);
        Assert.Equal(0, audio.OffsetSeconds);
        Assert.Null(audio.DurationSeconds);
        Assert.Null(audio.DetectedBpm);
    }

    [Fact]
    public void Normalize_GoodValues_AreKept()
    {
        var audio = new ShowAudio
        {
            FilePath = "a.mp3",
            RelativePath = "a.mp3",
            Volume = 0.3,
            FadeOutSeconds = 4,
            OffsetSeconds = -2.5,
            DurationSeconds = 200,
            DetectedBpm = 128,
            Muted = true,
        };

        audio.Normalize();

        Assert.Equal(0.3, audio.Volume);
        Assert.Equal(4, audio.FadeOutSeconds);
        Assert.Equal(-2.5, audio.OffsetSeconds);
        Assert.Equal(200, audio.DurationSeconds);
        Assert.Equal(128, audio.DetectedBpm);
        Assert.Equal("a.mp3", audio.RelativePath);
        Assert.True(audio.Muted);
    }

    [Fact]
    public void Show_NormalizeAudio_FixesBeatOffsetAndAudio()
    {
        var show = new Show { BeatOffsetSeconds = double.PositiveInfinity, Audio = new ShowAudio { Volume = -1 } };

        show.NormalizeAudio();

        Assert.Equal(0, show.BeatOffsetSeconds);
        Assert.Equal(0, show.Audio!.Volume);
    }

    [Fact]
    public void Clone_ChangingCopy_LeavesOriginalUntouched()
    {
        var original = new ShowAudio { FilePath = "a.mp3", RelativePath = "a.mp3", Volume = 0.5, DurationSeconds = 10 };

        var copy = original.Clone();
        copy.FilePath = "b.mp3";
        copy.Volume = 1;
        copy.DurationSeconds = 20;

        Assert.NotSame(original, copy);
        Assert.Equal("a.mp3", original.FilePath);
        Assert.Equal(0.5, original.Volume);
        Assert.Equal(10, original.DurationSeconds);
    }
}
