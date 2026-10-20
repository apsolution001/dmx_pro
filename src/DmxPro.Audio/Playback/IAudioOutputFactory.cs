namespace DmxPro.Audio.Playback;

/// <summary>Creates the output for a newly loaded song (one output per song, as WASAPI clients initialise once).</summary>
internal interface IAudioOutputFactory
{
    /// <exception cref="AudioException">No usable output device.</exception>
    IAudioOutput Create();
}
