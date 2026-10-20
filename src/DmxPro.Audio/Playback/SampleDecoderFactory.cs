namespace DmxPro.Audio.Playback;

/// <summary>
/// Opens a music file for playback. Songs up to <see cref="MaxInMemorySamples"/> are decoded completely into memory
/// (<see cref="MemorySampleDecoder"/>): Media Foundation's MP3 seeking is only approximate (measured: 15–60 ms off on a
/// 60 s file, and the error grows along the song), which would shift the lights after every seek; from memory every
/// seek is frame-exact. Longer files (DJ mixes) are streamed with <see cref="MediaFoundationDecoder"/>, whose seeks are
/// exact for WAV and within Media Foundation's accuracy for compressed formats.
/// </summary>
internal static class SampleDecoderFactory
{
    /// <summary>~18 minutes of 44.1 kHz stereo (192 MB at 16 bit).</summary>
    public const long MaxInMemorySamples = 96_000_000;

    /// <exception cref="AudioException">The file is missing, unsupported or damaged.</exception>
    public static ISampleDecoder Open(string path, CancellationToken cancellationToken)
    {
        MediaFoundationDecoder streaming = MediaFoundationDecoder.Open(path);
        try
        {
            MemorySampleDecoder? memory = MemorySampleDecoder.DecodeAll(streaming, MaxInMemorySamples, cancellationToken);
            if (memory is not null)
            {
                streaming.Dispose();
                return memory;
            }

            streaming.Seek(0);
            return streaming;
        }
        catch (Exception ex)
        {
            streaming.Dispose();
            if (ex is AudioException or OperationCanceledException)
            {
                throw;
            }

            throw new AudioException(
                $"This file cannot be played: \"{Path.GetFileName(path)}\" is damaged and could not be decoded ({ex.Message}).", ex);
        }
    }
}
