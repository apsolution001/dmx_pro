namespace DmxPro.Audio;

/// <summary>A music file could not be opened, decoded or played; the message is shown to the user.</summary>
public sealed class AudioException(string message, Exception? inner = null) : Exception(message, inner);
