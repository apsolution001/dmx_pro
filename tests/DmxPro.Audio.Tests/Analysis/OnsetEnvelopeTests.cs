using DmxPro.Audio.Analysis;

namespace DmxPro.Audio.Tests.Analysis;

public class OnsetEnvelopeTests
{
    [Theory]
    [InlineData(11025, 256)]
    [InlineData(22050, 512)]
    [InlineData(44100, 1024)]
    [InlineData(48000, 1024)]
    public void Constructor_SampleRate_PicksWindowOfAbout23Ms(int sampleRate, int expectedWindow)
    {
        var envelope = new OnsetEnvelope(sampleRate);

        Assert.Equal(expectedWindow, envelope.WindowSize);
        Assert.Equal(expectedWindow / 4, envelope.HopSize);
        Assert.InRange(envelope.FrameRate, 170, 190);
    }

    [Fact]
    public void Constructor_NonPositiveRate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OnsetEnvelope(0));
    }

    [Fact]
    public void Add_OneFramePerHop_PartialHopNotCounted()
    {
        var envelope = new OnsetEnvelope(11025);

        envelope.Add(new float[(envelope.HopSize * 10) + 5]);

        Assert.Equal(10, envelope.Count);
        Assert.Equal(((envelope.HopSize * 10) + 5) / 11025.0, envelope.DurationSeconds, 9);
    }

    [Fact]
    public void Add_Silence_AllZero()
    {
        var envelope = new OnsetEnvelope(11025);

        envelope.Add(new float[11025]);

        Assert.All(envelope.Values.ToArray(), value => Assert.Equal(0f, value));
        Assert.Equal(0f, envelope.PeakAmplitude);
    }

    [Fact]
    public void Add_Click_PeaksAtTheClickTime()
    {
        const int rate = 11025;
        var signal = new float[rate * 2];
        var clickAt = (int)(1.0 * rate);
        for (var j = 0; j < 300; j++)
        {
            signal[clickAt + j] = (float)(0.8 * Math.Sin(2 * Math.PI * 1000 * j / rate) * Math.Exp(-j / 150.0));
        }

        var envelope = new OnsetEnvelope(rate);
        envelope.Add(signal);

        var values = envelope.Values;
        var best = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best])
            {
                best = i;
            }
        }

        var time = envelope.FrameOffsetSeconds + (best / envelope.FrameRate);
        Assert.InRange(time, 1.0 - 0.01, 1.0 + 0.01);
        Assert.Equal(signal.Max(Math.Abs), envelope.PeakAmplitude);
    }

    [Fact]
    public void Add_NonFiniteSamples_TreatedAsSilence()
    {
        var envelope = new OnsetEnvelope(11025);

        envelope.Add([float.NaN, float.PositiveInfinity, float.NegativeInfinity, .. new float[2000]]);

        Assert.Equal(0f, envelope.PeakAmplitude);
        Assert.All(envelope.Values.ToArray(), value => Assert.True(float.IsFinite(value)));
    }
}
