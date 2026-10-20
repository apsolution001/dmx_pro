using DmxPro.Audio.Playback;

namespace DmxPro.Audio.Tests.Playback;

internal sealed class FakeAudioOutputFactory : IAudioOutputFactory
{
    public List<FakeAudioOutput> Created { get; } = [];

    /// <summary>When set, Create throws it (no audio device).</summary>
    public Exception? CreateError { get; set; }

    public FakeAudioOutput Last => Created[^1];

    public IAudioOutput Create()
    {
        if (CreateError is not null)
        {
            throw CreateError;
        }

        var output = new FakeAudioOutput();
        Created.Add(output);
        return output;
    }
}
