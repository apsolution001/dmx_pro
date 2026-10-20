using NAudio.Wave;

namespace DmxPro.Audio.Playback;

/// <summary>
/// The sound device seen by <see cref="AudioPlayer"/>. Device implementations live in <see cref="DeviceAudioOutput"/>;
/// tests inject a fake that pulls samples by hand, so no test ever plays sound.
/// </summary>
internal interface IAudioOutput : IDisposable
{
    /// <summary>Binds the sample source. Called once, before <see cref="Play"/>.</summary>
    /// <exception cref="AudioException">No usable output device.</exception>
    void Init(ISampleProvider source);

    /// <summary>Starts pulling samples (pre-filling the device buffer first). Output frame counting starts at 0.</summary>
    void Play();

    /// <summary>
    /// Stops and flushes the device; returns once the device no longer reads from the source. Must not be called from
    /// the device's own thread.
    /// </summary>
    void Stop();

    /// <summary>
    /// Frames (at the source's sample rate) the device has actually played since <see cref="Play"/>: the frame being
    /// heard now. Monotonic while playing (small backwards wobble is filtered by the player).
    /// </summary>
    long PlayedFrames { get; }

    /// <summary>The device stopped by itself (removed, driver error). Raised on any thread; handlers must not block.</summary>
    event EventHandler<Exception?>? Failed;
}
