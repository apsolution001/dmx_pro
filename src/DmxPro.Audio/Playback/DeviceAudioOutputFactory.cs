namespace DmxPro.Audio.Playback;

/// <summary>Creates <see cref="DeviceAudioOutput"/>s on the default Windows sound device.</summary>
internal sealed class DeviceAudioOutputFactory : IAudioOutputFactory
{
    public IAudioOutput Create() => new DeviceAudioOutput();
}
