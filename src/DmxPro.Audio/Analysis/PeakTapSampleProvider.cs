using NAudio.Wave;

namespace DmxPro.Audio.Analysis;

/// <summary>
/// Passes samples through unchanged while feeding them to a <see cref="WaveformPeakAccumulator"/>, so waveform peaks
/// come from the full-rate signal (before resampling removes short transients).
/// </summary>
internal sealed class PeakTapSampleProvider(ISampleProvider source, WaveformPeakAccumulator peaks) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        if (read > 0)
        {
            peaks.Add(buffer.AsSpan(offset, read));
        }

        return read;
    }
}
