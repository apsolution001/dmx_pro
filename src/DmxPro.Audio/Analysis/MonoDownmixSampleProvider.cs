using NAudio.Wave;

namespace DmxPro.Audio.Analysis;

/// <summary>Averages all channels of a float sample stream into one (any channel count, whole frames only).</summary>
internal sealed class MonoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _buffer = [];

    public MonoDownmixSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _channels = source.WaveFormat.Channels;
        if (_channels < 1)
        {
            throw new ArgumentException("The source has no channels.", nameof(source));
        }

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_channels == 1)
        {
            return _source.Read(buffer, offset, count);
        }

        var needed = count * _channels;
        if (_buffer.Length < needed)
        {
            _buffer = new float[needed];
        }

        var read = _source.Read(_buffer, 0, needed);
        if (read <= 0)
        {
            return 0;
        }

        // Sources normally return whole frames; if one stops mid-frame, complete it so channels never shift.
        while (read % _channels != 0)
        {
            var more = _source.Read(_buffer, read, _channels - (read % _channels));
            if (more <= 0)
            {
                break;
            }

            read += more;
        }

        var frames = read / _channels;
        var scale = 1f / _channels;
        var index = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < _channels; channel++)
            {
                sum += _buffer[index++];
            }

            buffer[offset + frame] = sum * scale;
        }

        return frames;
    }
}
