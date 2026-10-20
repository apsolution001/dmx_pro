using DmxPro.Audio.Analysis;
using NAudio.Wave;

namespace DmxPro.Audio.Tests.Analysis;

public class MonoDownmixSampleProviderTests
{
    [Fact]
    public void Read_Stereo_AveragesChannels()
    {
        var source = new ArraySampleProvider([1f, 0f, 0.5f, -0.5f, -1f, -1f], 44100, 2);
        var mono = new MonoDownmixSampleProvider(source);
        var buffer = new float[10];

        var read = mono.Read(buffer, 0, 10);

        Assert.Equal(3, read);
        Assert.Equal([0.5f, 0f, -1f], buffer[..3]);
        Assert.Equal(1, mono.WaveFormat.Channels);
        Assert.Equal(44100, mono.WaveFormat.SampleRate);
    }

    [Fact]
    public void Read_SixChannels_AveragesAll()
    {
        var source = new ArraySampleProvider([0.6f, 0.6f, 0f, 0f, 0f, 0f], 48000, 6);

        var buffer = new float[4];
        Assert.Equal(1, new MonoDownmixSampleProvider(source).Read(buffer, 1, 3));
        Assert.Equal(0.2f, buffer[1], 5);
    }

    [Fact]
    public void Read_SourceReturnsHalfFrames_KeepsChannelsAligned()
    {
        var source = new ArraySampleProvider([1f, 0f, 1f, 0f, 1f, 0f], 44100, 2, maxPerRead: 3);
        var mono = new MonoDownmixSampleProvider(source);
        var buffer = new float[3];
        var all = new List<float>();

        int read;
        while ((read = mono.Read(buffer, 0, 3)) > 0)
        {
            all.AddRange(buffer[..read]);
        }

        Assert.Equal([0.5f, 0.5f, 0.5f], all);
    }

    [Fact]
    public void Read_Mono_PassesThrough()
    {
        var source = new ArraySampleProvider([0.1f, 0.2f], 8000, 1);
        var buffer = new float[2];

        Assert.Equal(2, new MonoDownmixSampleProvider(source).Read(buffer, 0, 2));
        Assert.Equal([0.1f, 0.2f], buffer);
    }

    private sealed class ArraySampleProvider(float[] data, int sampleRate, int channels, int maxPerRead = int.MaxValue) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public int Read(float[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, maxPerRead), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
