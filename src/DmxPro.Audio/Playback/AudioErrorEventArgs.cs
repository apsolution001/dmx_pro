namespace DmxPro.Audio.Playback;

/// <summary>Why playback failed, in words for the user.</summary>
public sealed class AudioErrorEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}
