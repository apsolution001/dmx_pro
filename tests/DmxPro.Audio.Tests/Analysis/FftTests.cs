using DmxPro.Audio.Analysis;

namespace DmxPro.Audio.Tests.Analysis;

public class FftTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(1024)]
    public void PowerSpectrum_RandomSignal_MatchesNaiveDft(int size)
    {
        var random = new Random(size);
        var input = new double[size];
        for (var i = 0; i < size; i++)
        {
            input[i] = (random.NextDouble() * 2) - 1;
        }

        var power = new double[(size / 2) + 1];
        new Fft(size).PowerSpectrum(input, power);

        for (var k = 0; k <= size / 2; k++)
        {
            double re = 0;
            double im = 0;
            for (var n = 0; n < size; n++)
            {
                var angle = -2 * Math.PI * k * n / size;
                re += input[n] * Math.Cos(angle);
                im += input[n] * Math.Sin(angle);
            }

            var expected = (re * re) + (im * im);
            Assert.True(Math.Abs(expected - power[k]) <= 1e-9 * Math.Max(1, expected), $"bin {k}: {power[k]} vs {expected}");
        }
    }

    [Fact]
    public void PowerSpectrum_SineAtBin_PeaksAtThatBin()
    {
        const int size = 256;
        var input = new double[size];
        for (var i = 0; i < size; i++)
        {
            input[i] = Math.Sin(2 * Math.PI * 10 * i / size);
        }

        var power = new double[(size / 2) + 1];
        new Fft(size).PowerSpectrum(input, power);

        Assert.Equal(Math.Pow(size / 2.0, 2), power[10], 6);
        Assert.True(power[9] < 1e-9 && power[11] < 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(100)]
    public void Constructor_InvalidSize_Throws(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fft(size));
    }

    [Fact]
    public void PowerSpectrum_WrongBufferLength_Throws()
    {
        var fft = new Fft(8);

        Assert.Throws<ArgumentException>(() => fft.PowerSpectrum(new double[8], new double[4]));
    }
}
