namespace DmxPro.Audio.Playback;

/// <summary>
/// Decodes one music file to interleaved 32-bit float frames. Not thread-safe: the player calls it from one thread at
/// a time (the audio thread while playing), under its own lock.
/// </summary>
internal interface ISampleDecoder : IDisposable
{
    int SampleRate { get; }

    int Channels { get; }

    /// <summary>Length reported by the file (may be an estimate for VBR files).</summary>
    long LengthFrames { get; }

    /// <summary>
    /// Fills <paramref name="destination"/> (whole frames) with the next frames; returns the number of frames, 0 at the
    /// end. The frames returned by one call are contiguous and start at song frame <paramref name="firstFrame"/>.
    /// </summary>
    int Read(Span<float> destination, out long firstFrame);

    /// <summary>The next <see cref="Read"/> starts as close to <paramref name="frame"/> as the file allows.</summary>
    void Seek(long frame);

    /// <summary>Song frame the next <see cref="Read"/> starts at, when known (it is not known right after a seek).</summary>
    long NextFrame { get; }
}
