namespace DmxPro.Audio.Playback;

/// <summary>
/// Plays one music file. Safe to call from the UI thread; never blocks it for long (loading is async). Errors (file
/// missing, unsupported format, device lost) are reported through <see cref="Error"/>, never thrown from
/// Play/Pause/Seek.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Opens the file and prepares the output device. Throws <see cref="AudioException"/> when it cannot be played.</summary>
    Task LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The loaded file, or null.</summary>
    string? FilePath { get; }

    /// <summary>Length of the loaded song (zero when nothing is loaded).</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// What is being heard right now (the played position, latency-compensated), from the song start. The master clock
    /// while playing.
    /// </summary>
    TimeSpan Position { get; }

    bool IsPlaying { get; }

    /// <summary>Starts or resumes at <see cref="Position"/>. At or past the end nothing is heard.</summary>
    void Play();

    /// <summary>Pauses; <see cref="Position"/> stays.</summary>
    void Pause();

    /// <summary>Stops and returns to the start.</summary>
    void Stop();

    /// <summary>Jumps to <paramref name="position"/> (clamped to 0..Duration) while playing or paused, without an audible click.</summary>
    void Seek(TimeSpan position);

    /// <summary>Volume 0..1, applied smoothly (no zipper noise).</summary>
    float Volume { get; set; }

    /// <summary>
    /// Fades the music out linearly, sample-accurate, from song position <paramref name="start"/> over
    /// <paramref name="duration"/> to silence (silent after). A zero duration clears the fade.
    /// </summary>
    void SetFadeOut(TimeSpan start, TimeSpan duration);

    /// <summary>Playback failed (device removed, decode error). Raised on a background thread.</summary>
    event EventHandler<AudioErrorEventArgs>? Error;
}
